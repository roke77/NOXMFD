# TGP — optional high-quality mode (planning)

## Status

Planning only. No code yet. Default TGP behavior is the smooth
native-resolution path that ships today; this document describes a
future opt-in toggle for a sharper feed that intentionally costs
more in-game performance.

## Goal

Let the player choose a higher-quality TGP feed when they have the
GPU/CPU headroom to spend on it, without compromising the default
experience for anyone who doesn't. The two modes coexist; only one
is active at a time.

| Mode               | Source resolution | FPS cost     | Default?    |
|--------------------|-------------------|--------------|-------------|
| **Native** (today) | game's prefab (~360×240) | low (smooth) | yes |
| **High-quality**   | mirror cam at e.g. 720×480 or 1280×720 | noticeably higher | no — opt-in |

## Why this is a separate planning doc

We already tried the obvious implementation of high-quality (swapping
`TargetCam.cam.targetTexture` to a larger RT) and learned the hard
way that it has two costs:

- a noticeable FPS hit even when nobody is watching the feed (because
  it forces the game's cam+UICam to render at the larger size every
  frame, not just on capture ticks); and
- it **repositions the in-cockpit targeting overlay** (the white box +
  red crosshair) because the UI canvas snaps to the swapped RT's
  dimensions instead of the prefab's.

The second is a UX dealbreaker: even if the player accepts the FPS
cost, they shouldn't lose their in-cockpit targeting reticle to opt
into a sharper web feed. So if we add this mode at all, it has to be
done a different way.

## Approach: mirror camera

Instead of redirecting the game's existing TargetCam, spawn **our
own** `Camera` as a sibling on the same mount point.

- Read `TargetCam.GetCamMount()` for the active mount (forward / rear /
  landing) and parent the mirror cam there.
- Copy the private `cam.fieldOfView` from the game's `TargetCam` each
  frame so the mirror tracks the same zoom-on-target behavior.
- (Optional) copy IR state so the mirror reflects `SwitchIRState`.
- Render our cam to **our own** RenderTexture at the chosen high-res
  size (configurable; start with 720×480).
- Do NOT touch the game's `TargetCam.cam`, `UICam`, viewport rects,
  aspect, or material. The in-cockpit screen stays exactly as
  vanilla — the cam and UI canvas it depends on are completely
  unmodified.
- Feed the high-res RT into the same `Graphics.Blit` → `AsyncGPUReadback`
  → `EncodeToJPG` pipeline the native path uses today.

Tradeoff: this is a second camera pass on the GPU every frame the
mode is active. That's the "costly" part — it's why this mode is
opt-in. A second 720p pass on a target-scene render is significant
on lower-end hardware, which is exactly why the default should never
turn it on automatically.

## What we deliberately don't do

- We **don't reuse the swap-based path** (overriding
  `cam.targetTexture` and `UICam.targetTexture`). Even with the
  toggle off it requires extra wiring, and even with the toggle on
  it would break the in-cockpit overlay positioning. Mirror cam is
  the only path that satisfies "high-res web feed AND vanilla
  cockpit display."
- We **don't fall back automatically** based on framerate. The user
  is choosing the mode explicitly; we don't second-guess them.

## How the player toggles the mode

Three options, ordered by implementation cost. Pick during
implementation, not now.

### Option 1 (MVP): BepInEx `ConfigEntry` via Configuration Manager

We already ship with `BepInEx.ConfigurationManager` installed. Add a
single config entry:

```csharp
public enum TgpQuality { Native, HighQuality }
internal static ConfigEntry<TgpQuality> TgpQualityConfig;

TgpQualityConfig = Config.Bind(
    "TGP", "Quality", TgpQuality.Native,
    "Native = game's prefab resolution, smooth. HighQuality = mirror camera at 720×480, sharper but adds an extra render pass per frame.");
```

Player opens F1 (Configuration Manager hotkey), changes the dropdown,
mode swaps without reloading the game. Hook
`TgpQualityConfig.SettingChanged` to call a `SetMode(...)` on the
reader that engages/disengages the mirror cam.

Pros: zero MFD-side work, two-line UX.  
Cons: discoverability — players who don't know F1 won't find it.

### Option 2: MFD button on the TGP page

Add a labeled key on the TGP page (e.g. `QLT` next to MAIN) that
cycles through Native / HighQuality. Send a `quality` message via
the existing postMessage protocol; the page persists the choice
to `localStorage`; the server-side mode actually changes via a new
endpoint (e.g. `POST /tgp/quality`) or a query param on `/tgp.mjpg`
(`/tgp.mjpg?q=hq`).

Pros: discoverable, lives on the same surface as the feed itself.  
Cons: more wiring across MFD HTML/JS, server, and reader.

### Option 3: query param on `/tgp.mjpg` only

Drop the UI entirely. Subscribing to `/tgp.mjpg?q=hq` engages the
mirror cam; `/tgp.mjpg` engages native. The MFD client decides which
URL to use.

Pros: cleanest server contract — quality follows the subscriber.  
Cons: still needs a way for the player to flip the MFD client, so
this collapses into Option 2 anyway from the user's POV.

**Recommendation:** ship Option 1 first (smallest diff, real toggle).
Add Option 2 later if discoverability is a problem.

## State machine

Three states, one per mode plus idle:

```
Idle  ─── (subscriber appears) ──▶  Native
Idle  ─── (subscriber appears, config=HQ) ──▶  HighQuality
Native ──── (config change → HQ) ──▶  HighQuality   (engage mirror cam)
HighQuality ── (config change → Native) ──▶  Native (release mirror cam)
Native / HighQuality ── (last subscriber leaves) ──▶  Idle
```

Both Native and HighQuality use the same readback + encode +
gating + Disengage path that already exists; the only thing the mode
governs is the **source texture** the readback reads from:

- Native: `tc.cam.targetTexture` (game's own RT) — today's behavior.
- HighQuality: our mirror cam's RT.

So the mode switch is, in code: "swap the `Texture src` we Blit from,
and engage/release the mirror cam GameObject." Everything downstream
is shared.

## Implementation sketch (when we get to it)

1. **Mirror cam controller.** A small helper class that, given a
   `TargetCam`, parents a mirror `Camera` to the active mount,
   matches `fieldOfView` each tick, renders to a new high-res RT,
   and can be cleanly disposed.
2. **Config entry** in `Plugin.cs` (Option 1 above). Subscribe to
   `SettingChanged`; forward the new value to the reader.
3. **Reader switches `src`.** In `CaptureTgpFrame`, after the
   subscriber gate but before the size/Blit logic:
   - If mode == HighQuality, ensure mirror cam exists (engage if not),
     update its fov/mount, set `src = mirror.targetTexture`.
   - If mode == Native, ensure mirror cam is released, set
     `src = tc.cam.targetTexture` (today's path).
4. **Disengage releases the mirror cam too** — extend the existing
   `DisengageTgp()` to call into the mirror cam controller's
   teardown.
5. **Preview harness.** The `tools/serve_web.py` http harness can supply a
   mock quality field if we want to show a mock toggle, but it doesn't need
   to render anything different — the MFD pane treats both modes the same.

## Open questions to settle while implementing (not now)

- Default HQ resolution. 720×480 keeps aspect with the native source;
  1280×720 is sharper but doubles the fragment cost again. Probably
  720×480 as the first ship and let the player crank it via a second
  config entry if they want.
- Does the mirror cam need its own `UICam` to draw the overlay
  (mag/dist/grid/mode)? Almost certainly no — we'd render those
  overlays on the MFD client from the SSE snapshot, which is free.
- Does the mirror cam need to render every frame, or can we render it
  only on capture ticks (15 Hz)? `Camera.Render()` on demand would
  cut the GPU cost dramatically. Worth investigating during impl.
- IR mode: does the mirror cam need its own post-process to match the
  game's IR look, or can we read the same shader values off the
  TargetCam? Cosmetic detail, defer.

## Out of scope

- Actually building any of this. This document is plan-only.
- Changing the default mode. Native stays default.
- Removing the current gating or async-readback work — both apply to
  HighQuality unchanged.
- The legacy swap approach. Don't revive it; mirror cam supersedes it.

## Pre-flight before implementing

- Read `src/plugin/TgpFeed.cs` — it maps the relevant `TargetCam`
  internals (cam / mount / fov reflection) and implements the subscriber
  gating + async readback this mode has to carry over. (The original
  `tgp-camera-feed.md` / `tgp-feature-gating.md` design docs were removed
  once the feed shipped — `TgpFeed.cs` is the source of truth now.)
- Re-read this doc and pick a toggle option before writing any code.
