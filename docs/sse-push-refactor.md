# Change-gated SSE state instead of per-page polling

## Goal

SQD, WPT, TGT's leader-only TD column, and `td-nav.js`'s own "is this pilot in a squad" check each
independently polled `GET /squad` on their own timer (1-2s); TGT and `td-nav.js` also each polled
`GET /td-state`; `waypoints-store.js`'s top-window instance polled `GET /wpt-options` every 1.2s.
Four-plus independent HTTP round trips a second for data that changes rarely, when the plugin
already computes and change-gates all three as `volatile` snapshot strings (`Squad.StateJson`,
`TdStore.StateJson`, `RouteStore.RoutesJson`) the exact same way `Squadron`'s own shared-data
payloads do.

The fix: push all three over the SSE `/stream` connection every browser tab already keeps open for
telemetry (`SseHub.cs`) — the same connection the cursor/telemetry-frame events already ride.
Nothing here changes the underlying *feature* — same commands, same server state — only how a
browser learns the state changed.

## Server: `SseHub.cs`

Three new named events, each change-gated per connection exactly like the existing `event: sqd`
squad-state push already was (added earlier, but never consumed):

- `event: sqd` — `{"ready":<bool>,"state":<Squad.StateJson>}`, same wrapper shape `GET /squad`
  already returns (`SquadEndpoint.ServeSquad`), so a client's parsing code doesn't need to
  special-case the push vs. its own initial fetch.
- `event: td-state` — same shape, wrapping `TdStore.StateJson`.
- `event: wpt-options` — bare `RouteStore.RoutesJson`, unwrapped — matches `GET /wpt-options`
  exactly (`ConfigEndpoint`'s handler serves that string as-is, no `{ready,state}` wrapper).

Each has its own per-connection `lastX` cursor (`lastSquad`/`lastTd`/`lastWpt`), same
latest-value-wins comparison the existing cursor/sqd events use — a snapshot, not a queue, so a
display only needs the newest value, not every intermediate one.

## Browser: `telemetry-source.js` → shell → page

`telemetry-source.js` (the one `EventSource('/stream')` connection, always alive in MAP's
always-loaded tap iframe/mapFrame regardless of which page is visible) relays all three straight up
via `postMessage` — this tap only ever relays, never interprets:

- `sqd` → `{type: 'sqd-state', data: {ready, state}}`
- `td-state` → `{type: 'td-state-push', data: {ready, state}}`
- `wpt-options` → `{type: 'wpt-options-push', data: <routes>}`

The shell then fans each out to whichever page/pane actually needs it:

- **Classic shell (`mfd.js`)** — new `sqdStateData`/`tdStateData` caches (mirroring every other
  `xData` cache already there), and `forwardSqdStateToFrame/Panes`/`forwardTdStateToFrame/Panes`
  helpers. Unlike the existing `forward*ToPanes(page, payload)` helpers (single page), these fan out
  to every page that needs it — `SQD_STATE_PAGES = ['sqd','wpt','tgt','td']`,
  `TD_STATE_PAGES = ['tgt','td']` — since squad state has more than one consumer at once. Both the
  pane-load and full-view-frame-load handlers push whatever's cached the moment a relevant page
  loads, same "the page may have been mid-update when its iframe started loading" reasoning every
  other page-load catch-up here already has.
- **F-35 shell (`f35.js`)** — no new machinery needed at all: `sqd-state`/`td-state-push` aren't
  specially handled anywhere in the message dispatcher, so they fall straight through to the
  existing generic `slices[m.type] = m; livePortals().forEach(p => p.onSlice(m.type))` path (the
  exact one `targets`/`rwr`/`tgp`/etc. already use). Adding `'sqd-state'`/`'td-state-push'` to the
  relevant `PAGE_FEEDS[page]` entries is the entire change — the generic `forwardToPage()`/
  `forwardSlice()` machinery already replays cached slices to a freshly-loaded portal for free.

`wpt-options-push` needed no shell-side forwarding logic at all in either shell:
`waypoints-store.js` is loaded directly into the shell's own top document (`mfd.html`/`f35.html`,
not a page iframe), so it can listen for the message directly on `window` — no relay hop needed.
Its existing `wptroutes:changed` → `forwardWptRoutesToPanes/Frame/Map` chain (built for WPT/MAP's
own route-library sync) is completely unchanged; only what *feeds* it changed.

## Consumers

Every consumer keeps exactly one **bootstrap fetch** on load — covering the brief gap before the
first push arrives, and standalone/preview contexts with no shell/telemetry-source at all (a direct
`/wpt` or `/map-view` route in `serve_web.py`, which has no MAP tap running to relay anything) — then
switches entirely to a `window.addEventListener('message', ...)` listener for updates. No consumer
runs a recurring `setInterval` for this data anymore:

- **`sqd.js`** — `applySquad(s)`/`applyPlayers(list)` extracted from the old `refreshSquad().then()`/
  `refreshPlayers().then()`; one bootstrap fetch each, then `'sqd-state'`/`'server-players-push'`
  listeners (see "Follow-up: match roster" below — `GET /server-players` originally kept its own
  2s poll).
- **`wpt.js`** — same `applySquad(s)` extraction for its pending-share-button gate.
- **`tgt.js`** — `applySquad`/`applyTdState`, one bootstrap fetch each, for the leader-only TD
  column.
- **`td-nav.js`** — `apply(NAV, s)` extracted from the old `poll(NAV)`; runs in the shell's own top
  document (like `waypoints-store.js`), so it listens directly with no relay hop.
- **`waypoints-store.js`** — the top window's `setInterval(poll, 1200)` is gone; one `poll()` at
  load, then a `'wpt-options-push'` listener. Every embedded (non-top) instance was already
  push-based via the pre-existing `wpt-routes-request`/`wpt-routes` handshake — untouched.

`td.js` was left unchanged in this original pass — it already fetched only on load/REFRESH/an
explicit nudge (`docs/target-designator.md`'s "A static table, on purpose"), which is a one-shot,
event- or user-triggered GET, not a recurring poll, and looked like the end state every other page
here was converging toward. It turned out to still have a real gap — see "Follow-up" below.

## Follow-up: `td.js` closes the loop, and the squadron round-trip is gone

Two things found after this refactor first shipped:

1. **A member with TD already open didn't see a fresh DESIGNATE land.** The reactive nudge
   (`applySquadronPayload`'s `td.designate` branch → `sendCommand('td.receive-designation', ...)` →
   a `'td-designation-received'` postMessage → `td.js` re-fetching `/td-state`) raced the plugin's
   own command queue: the re-fetch could run before `CommandDispatcher.Drain()` had actually applied
   the designation on the Unity main thread, so it came back stale. Fixed by having `td.js` listen
   for the `'sqd-state'`/`'td-state-push'` messages directly — the same ones `tgt.js`'s TD column
   already rode — instead of the fetch-after-nudge round trip. The `'td-squad-ended'` nudge
   (`td-nav.js`'s edge detection, `mfd.js`/`f35.js`'s `nudgeTdPage`) became redundant the same way
   once `td.js` was listening to `'sqd-state'` directly, and was removed.
2. **The leader-shared-payload round trip itself was unsound**, not just racy for TD specifically.
   `Squad.HandleData` only ever *queued* an incoming `wpt.route`/`wpt.route-deleted`/`td.designate`
   payload (the `event: squadron` SSE push, since removed) for some browser tab to notice and POST
   back as a command (`wpt.receive-shared`/`wpt.remove-shared`/`td.receive-designation`, also since
   removed). That meant a payload arriving with no browser connected — or before any tab had ever
   connected — was lost forever (a fresh SSE connection's cursor started at "now," never replaying a
   backlog), and a payload arriving while several tabs were open got applied once per tab. Fixed by
   having `Squad.HandleData` apply all three known payload types directly, synchronously, on the
   same main-thread `Drain()` call that received them (`RouteStore.ReceiveSharedRoute`/
   `RemoveSharedRoute`, `TdStore.ReceiveDesignation`) — every open display then learns the result
   through the ordinary `td-state`/`wpt-options` state-change push this doc already describes, same
   as any other plugin-side mutation. The `event: squadron` SSE event, its `telemetry-source.js`
   listener, `applySquadronPayload` in both shells, and the three now-unreachable commands are gone.

## Harness (`tools/preview-mock.js`)

The mock's `MockEventSource` originally had no state events at all. It now calls the preview-only
`/__preview-push` endpoint, which hashes SQD/TD/WPT/keybind/HUD snapshots and returns only changed
payloads, then fires the corresponding synthetic SSE events. This is invisible to the page code
under test, which only listens for the events, and avoids recreating one timer per REST endpoint.

**ponytail**: 1.5s cadence, not faster — `serve_web.py` opens a fresh TCP
connection per `fetch()` with no keep-alive, and each sits in Windows' TIME_WAIT for ~2 minutes
afterward; a tighter interval across a long testing session exhausts the local ephemeral port range
(`ERR_NO_BUFFER_SPACE`) even though the real plugin's persistent SSE connection has no such cost at
all. Upgrade path if this still isn't gentle enough for a long session: add real HTTP keep-alive,
or lengthen this further.

## Follow-up: keybind configuration and HUD options

The same transport now carries two additional rare-change snapshots:

- `keybinds-config` is guarded by monotonic versions in `Keybinds` and `ImmersionConfig`. The
  roughly 16 KB registry is built per connection only on connect or after a real bind, capture, or
  setting mutation. `remote-keybinds.js` fans the shell's initial SSE snapshot into its child
  documents (and performs a one-shot GET only for standalone or stream-timeout fallback contexts);
  `layout-keybinds.js` reads the same push.
  The KEY page retains a 600 ms poll only while hardware capture is armed, as a standalone fallback.
- `hud-options` compares the cached JSON already produced by `TelemetryServer`'s 1 Hz game scan and
  emits only on a changed string. Both shells cache/replay it to HUD frames and portals; `hud.js`
  also change-gates rendering, preventing a full category/icon/list rebuild for identical data.

The preview's synthetic EventSource uses one `/__preview-push` request containing hashes and only
changed payloads. That keeps the harness faithful without recreating five independent endpoint
polls in browser diagnostics; the recurring request remains preview-only.

## Follow-up: match roster and idle-tick `Flush()`

Two more items from the original polling audit:

- `server-players` (SQD's invite-candidate list, `PlayerRoster.Json`) now rides the same
  change-gated transport as `sqd`/`td-state`/`wpt-options` — bare JSON, no `{ready,state}` wrapper,
  same latest-value-wins string comparison. `sqd.js` keeps one bootstrap `GET /server-players` and
  drops its 2s poll in favor of a `'server-players-push'` listener; both shells relay it only to a
  page/pane actually showing SQD, the same single-page shape `hud-options-push` uses.
- `SseHub.cs`'s per-connection loop ticks at `CursorTickMs` (~60 Hz) and previously called
  `ctx.Response.OutputStream.Flush()` unconditionally every tick, even when nothing above it had
  written anything (the common case between the 10 Hz telemetry-frame beat). Each write site now
  sets a per-tick `wrote` flag, and `Flush()` only runs when `wrote` is true — cutting idle-tick
  flush calls by roughly 5/6 per connected display without changing what gets sent or when.

## Verification

`dotnet build` (0 errors), `dotnet test` (212/212, unchanged — this refactor has no plugin-side
logic beyond the three new SSE emits, which are string-comparison based like the existing `sqd`
event they extend). Full `*.test.js` suite green, including `sse-event-coverage.test.js` — extended
from checking one hardcoded event name to scanning every `"event: <name>\n"` literal in `SseHub.cs`
and asserting each has a matching `telemetry-source.js` listener, catching exactly the class of bug
that shipped once already (server emits one name, client listens for another, and the data silently
never arrives).

Live-verified in the `serve_web.py` harness: with a `fetch` spy installed inside SQD's and TGT's
iframes, issuing `sqd.create` via `POST /command` updated both pages' rendered state (full roster on
SQD, `has-td-col` flipping true on TGT) with **zero** `/squad` or `/td-state` fetches recorded
afterward — confirming the update came entirely from the relayed push, not a poll.
