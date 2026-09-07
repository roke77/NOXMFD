# Internal MFD — native cockpit rendering of NOXMFD pages

## Status

**Proof-of-concept in progress**, `feature/internal-mfd-poc`. `Cockpit`/`TacScreen`/`VirtualMFD` are
decompiled and confirmed to exist as described below. `InternalMfdPoc.cs` inserts a placeholder
panel as the last sibling of `TacScreen`'s `Canvas`, toggled by a keybind (`internal-mfd-poc-toggle`,
unbound by default) — not yet live-verified in-game.

## Goal

Today NOXMFD's pages (RDR, WPN, TGT, AKF, HUD, ...) only exist as web pages served to an external
browser/MFD. This is about showing some of that same *content* — radar, aircraft gauges, pylons —
**inside the game's own in-cockpit MFD screen**, replacing what the game normally draws there, for
players who want that information natively in the cockpit instead of (or in addition to) an
external display.

This is explicitly **not** "stream the web page into the cockpit." It's a from-scratch native
Unity re-render of the relevant page content, even though that duplicates logic already written
once in HTML/CSS/JS. That duplication is accepted going in — see [Why native, not
screen-scraped](#why-native-not-screen-scraped).

## Feasibility approach

The candidate insertion point is the game's own `Cockpit.tacScreen.canvas`. A proof-of-concept must
resolve that `Canvas` and insert a `GameObject` with a `RawImage` as its **last sibling**, at the top
of the paint order:

```csharp
var tac = tacScreenField.GetValue(cockpit);
mfdCanvas = canvasField.GetValue(tac) as Canvas;
...
active.overlayGO = new GameObject($"MFDOverlay_{slotName}");
active.overlayGO.transform.SetParent(mfdCanvas.transform, false);
active.overlayGO.transform.SetAsLastSibling();
active.rawImg = active.overlayGO.AddComponent<RawImage>();
```

The overlay owns its `RenderTexture` or `Texture2D` and destroys the inserted object when disabled.
Canvas content draws after cameras, so this can cover the native camera and `UICam` output without
mutating camera state.

If replacement also requires suppressing native drive logic, use guarded Harmony **prefix** patches
on the relevant per-frame methods:

```csharp
[HarmonyPrefix] static bool Prefix() => !Plugin.ManualMode;
```

While the flag is set, the game's own per-frame logic does not race the replacement. This preserves
the reset behavior documented in [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md).
Both the canvas path and each native drive method still require verification against the current
game assemblies.

## Why native, not screen-scraped

The alternative — capture the existing web pages' rendered frames (headless browser / CEF-style
capture) and blit them onto the same `RawImage` — was considered and rejected:

- Adds a whole capture pipeline (a browser process, frame transport, texture upload) for content
  NOXMFD's plugin already has the underlying data for directly.
- Adds latency and a new failure mode (browser crashes/hangs → cockpit MFD content silently
  freezes or blanks) exactly where the goal is a small, reliable perf win.
- The web pages are laid out for a browser viewport at typical MFD proportions; the cockpit MFD's
  actual `RawImage` geometry is unlikely to match without its own layout work anyway, so little of
  the CSS/HTML actually transfers even if captured.

Rendering natively with Unity UI, driven straight from the same `TelemetrySnapshot`/store data the
HTTP server already exposes, avoids all of that. The cost is explicit: gauge layout, radar sweep
math, pylon iconography, etc. get a second implementation, this time in C# UI code instead of
HTML/CSS/JS. Accepted per the decision to pursue this doc.

## Candidate content

Matches the user's framing — content that could plausibly replace what the native cockpit MFD
shows today:

| Native cockpit shows (today) | NOXMFD equivalent | Notes |
|---|---|---|
| Radar picture | `RDR` page | Sweep/contact rendering — highest native-redraw cost of the three |
| Aircraft gauges (throttle/gear/status tiles) | `AVN` page | Mostly static layout + numeric/needle updates — cheapest to port, and the current POC's target |
| Pylon/loadout display | `WPN` page | Icon-per-station grid, already fairly static-shaped |

Scope for a first pass: **one** page at a time replacing the cockpit MFD content, with some way
to cycle which one is shown (see [Toggle / page selection](#toggle--page-selection)) — not a
faithful in-cockpit reproduction of NOXMFD's full split-view/paging shell.

## Investigation needed before implementation

1. **What `Cockpit.tacScreen.canvas` actually contains** — resolved by decompile: `Cockpit` holds a
   private `TacScreen tacScreen` field (populated at runtime, local player's aircraft only);
   `TacScreen` holds a private `Canvas canvas` plus its own `Camera cam` and `RenderTexture
   renderTexture`. One shared `MonoBehaviour` type across aircraft, not a per-airframe subclass —
   but whether every airframe's `tacScreenUIPrefab` wires those fields identically (geometry,
   `cam.cullingMask`, canvas render mode) is still unconfirmed; a newly-created child defaults to
   Unity's layer 0, which is *not* automatically the canvas's own layer, so a camera with a
   restricted culling mask could resolve the canvas correctly and still never render an inserted
   child. `InternalMfdPoc.cs` sets the child's layer to match the canvas's own as a defensive
   measure, but whether that's sufficient is exactly what the live test below settles.
2. **What drives the *other* cockpit MFD content** (radar sweep, gauge needles, pylon icons) — the
   equivalent of `TargetCam.SetTargetCam()`/`AimCamera` for the TGP feed. Each of radar/gauges/
   pylons likely has its own driving method(s) that would need their own Harmony prefix guard,
   not necessarily the same one. Still open — out of scope for the current placeholder-only POC,
   which covers its content rather than suppressing what drives it.

## Toggle / page selection

Implemented for the POC: `internal-mfd-poc-toggle` (`Keybinds.cs`, `DefFree`, off-by-default,
unbound until set on `/keybinds`), with a matching `/command` case
(`internal-mfd.poc-toggle`) so a remote keybind press also works. Restoring native content is
handled for toggle-off, aircraft change (an aircraft-identity check, not just a fake-null check on
the cached `Canvas`), and mission exit (the static enabled flag resets in `OnDestroy`, which fires
when `MissionLifecycle` tears down the mission-scoped reader). Not yet handled: any restore-on-
init-failure case beyond "the overlay object is simply never cached and the next frame retries."

## Open questions

- Single canvas/insertion-point assumption: does every playable airframe expose
  `Cockpit.tacScreen.canvas` (or an equivalent) the same way, or does this need per-aircraft
  handling?
- Camera culling mask / canvas render mode: does `TacScreen`'s dedicated camera actually render an
  inserted last-sibling child, and does matching the canvas's own `GameObject.layer` fully cover
  it? Unverified until the POC is run live — see [Status](#status).
- Performance: is native Unity UI redraw of a radar sweep actually cheaper than the status quo, or
  does it just move cost from "duplicate camera render" to "duplicate UI redraw"? No profiling
  done yet.
- Do the driving methods for radar/gauges/pylons need the same "invoke the game's own toggle
  event" treatment `tgp-suppress-native-render.md` uses (cosmetic-only, camera/renderer untouched),
  or does full content replacement need guarded Harmony-prefix suppression?

## Out of scope (for this doc)

- Full split-view/paging parity with the external MFD shell inside the cockpit.
- Aircraft other than whatever the POC confirms works cleanly.
- Changes to `tgp-suppress-native-render.md`'s TGP-specific work — related precedent, separate
  feature.

## Related

- [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md) — same cockpit-MFD problem
  space (native TGP camera), same `SetTargetCam()` reset hazard, currently-shipping cosmetic-only
  answer for that one feature.
