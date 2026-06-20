# TGP feature gating — capture only when the MFD is watching

## Goal

Pay the TGP capture cost (render-target swap, GPU readback, JPEG
encode) only while a client is actually subscribed to the
`/tgp.mjpg` stream. When the MFD's TGP page is not open, the
pipeline stays completely idle and the game runs as if the mod
weren't capturing anything.

## Why now

Locking a target engages the capture pipeline today, regardless of
whether anyone is watching the feed. That causes a measurable FPS
drop (see `tgp-fps-investigation.md`). The cheapest fix is also the
most correct one: don't do the work when it has no consumer.

## What the client already does (no MFD changes needed)

`src/MfdPage.cs`:

- `showPage('tgp')` sets `tgpImg.src = '/tgp.mjpg'`.
- Leaving the TGP page (any other `showPage(...)`) removes the src.

So the browser already opens the MJPEG connection when the user
visits TGP and closes it when they leave. We don't have to add any
client-side ping or heartbeat — TCP teardown is the signal.

## Server side — subscriber counter

`src/TelemetryServer.cs`:

- Add `private static int _tgpSubscribers;`.
- In `HandleMjpegAsync`: `Interlocked.Increment` on entry,
  `Interlocked.Decrement` in the existing `finally`. Both the normal
  client-disconnect path and the cancellation path go through that
  `finally`, so the counter stays accurate.
- Expose `public static bool WantsTgpFrames => Volatile.Read(ref _tgpSubscribers) > 0;`.

That's the whole server change. A handful of lines.

## Reader side — capture path gating

`src/TelemetryReader.CaptureTgpFrame`:

- At the very top of the method, check `TelemetryServer.WantsTgpFrames`.
- If `false` and we currently have an active swap (`_tgpSetupForTc != null`),
  call a new `DisengageTgp()` helper.
- If `false` and we're already idle, just return — no allocations,
  no reflection, no game-side calls.
- Otherwise (subscribers > 0), continue with the existing path
  unchanged.

`DisengageTgp()` is the clean teardown that mirrors the swap setup:

- Read the current `Camera` and `UICam` from the cached reflection
  fields; restore their `targetTexture` to `_tgpOrigCockpitRT` (the
  game's original RT, captured at swap time on line ~577). After
  this point the in-cockpit screen runs vanilla again at the
  original render size — the very thing that costs less.
- Release/destroy `_tgpCamRT`, `_tgpRT`, `_tgpTex`. Re-allocating
  them on next engage is cheap and avoids holding ~MB of RTs while
  idle.
- Reset `_tgpSetupForTc = null`, `_tgpOrigCockpitRT = null`,
  `_tgpActive = false`, `_tgpSrcLogged = false`.
- Call `TelemetryServer.ClearTgpFrame()` so any client that
  re-subscribes after the gap sees "no feed" until the next capture
  pushes one.

Re-engage just falls out of the existing code: the next time
`WantsTgpFrames` is true, the method proceeds past the gate, hits
the `if (!ReferenceEquals(_tgpSetupForTc, tc))` block, and rebuilds
the swap exactly as today.

Also call `DisengageTgp()` from `OnDestroy()` so we leave the cam
pointing at a valid RT on mission end instead of one we're about to
destroy.

## State machine, end to end

| Subscribers | Target locked | Outcome                                  |
|-------------|--------------|------------------------------------------|
| 0           | no           | gate out; nothing runs                   |
| 0           | yes          | gate out + DisengageTgp if still engaged; cam runs vanilla cockpit-only |
| >0          | no           | gate passes; existing "no targetCam → clear frame" path |
| >0          | yes          | gate passes; full capture path (today's behavior) |

The cost of being on the TGP page with no target locked is just the
cockpit display's normal work plus our 33 ms cadence of doing
nothing — negligible.

## Thread model

- `WantsTgpFrames` reads an `int` published by `HandleMjpegAsync`
  (worker threads). One reader, multiple writers, plain
  `Interlocked.*` is sufficient — no lock.
- The actual texture swap / unswap happens on the Unity main thread
  inside `CaptureTgpFrame`, where it always has.

## Open question

- If a client briefly disconnects (page reload, navigated away then
  back) we'll tear down and rebuild the swap. Should be cheap, but
  worth logging the engage/disengage transitions while we measure so
  we can confirm we're not thrashing.

## Out of scope

- Changing capture resolution/frame rate. That's `tgp-fps-investigation.md`.
- Async GPU readback. Same.

## Acceptance

- Open a mission, do not visit the TGP page on the MFD: in-game FPS
  with a target locked matches in-game FPS unlocked.
- Visit TGP page on MFD: capture engages within one tick, feed
  displays, FPS drop returns (and is now the *only* place we'd see
  it, until the investigation reduces it).
- Leave the TGP page: feed clears within one tick, FPS recovers.
