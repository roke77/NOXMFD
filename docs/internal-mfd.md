# Internal MFD — native cockpit rendering of NOXMFD pages

## Status

**Planning.** No branch, no code. Written after investigating how two other Nuclear Option mods
(`9138noms/MFDCustomizer`, `9138noms/TargetCamControl`) manipulate the in-cockpit MFD, to confirm
this is feasible before committing to it.

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

## Precedent — this is provably possible

Two existing third-party mods informed this doc:

**`MFDCustomizer`** (single-file plugin, `Plugin.cs`) replaces cockpit MFD content today, in
shipping form. It never touches a camera. It reflects into the game's own UI hierarchy —
`Cockpit.tacScreen.canvas` — grabs that `Canvas`, and inserts a new `GameObject` with a `RawImage`
as its **last sibling** (top of paint order):

```csharp
var tac = tacScreenField.GetValue(cockpit);
mfdCanvas = canvasField.GetValue(tac) as Canvas;
...
active.overlayGO = new GameObject($"MFDOverlay_{slotName}");
active.overlayGO.transform.SetParent(mfdCanvas.transform, false);
active.overlayGO.transform.SetAsLastSibling();
active.rawImg = active.overlayGO.AddComponent<RawImage>();
```

It feeds that `RawImage` a `RenderTexture`/`Texture2D` of its own choosing, and destroys the
`GameObject` when done. Because UI canvas content always draws after all cameras in Unity's
standard render order, this fully occludes/replaces whatever the native cameras rendered
underneath — including the separate `UICam` overlay camera — with no camera-state interaction at
all.

**`TargetCamControl`** (public source, `Plugin.cs` + `Runner.cs`) confirms the companion technique
for *suppressing the game's own drive logic* without fighting its resets. It reflects
`TargetCam.SetTargetCam()` and `TargetCam.CancelTarget()` directly, and uses Harmony **prefix**
patches on the methods that normally drive the camera each frame (`AimCamera`, `Update`,
`SwitchIRState`), guarded by a mode flag:

```csharp
[HarmonyPrefix] static bool Prefix() => !Plugin.ManualMode;
```

While the flag is set, the game's own per-frame logic never runs, so there's nothing to fight —
no reset-on-`!enabled` race like the one documented in
[`tgp-suppress-native-render.md`](tgp-suppress-native-render.md).

Combined, these two mods demonstrate the two halves this feature needs: a confirmed insertion
point for native content on top of the cockpit MFD (`Cockpit.tacScreen.canvas`), and a confirmed
pattern for cleanly taking over from the game's own driving logic instead of racing it.

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
| Aircraft gauges (RPM/FUEL/HEAT/THRL etc.) | `AKF` page | Mostly static layout + numeric/needle updates — cheapest to port |
| Pylon/loadout display | `WPN` page | Icon-per-station grid, already fairly static-shaped |

Scope for a first pass: **one** page at a time replacing the cockpit MFD content, with some way
to cycle which one is shown (see [Toggle / page selection](#toggle--page-selection)) — not a
faithful in-cockpit reproduction of NOXMFD's full split-view/paging shell.

## Investigation needed before implementation

This doc stops short of an implementation sketch because two things are still unknown and must be
decompiled/checked first:

1. **What `Cockpit.tacScreen.canvas` actually contains**, and whether it's one canvas per
   aircraft type or a shared structure — `MFDCustomizer` only proves an overlay can be inserted,
   not what native elements exist underneath per airframe (relevant: the T/A-30 renderer-scope
   surprise already hit once in `tgp-suppress-native-render.md`, where a renderer boundary reached
   further than expected).
2. **What drives the *other* cockpit MFD content** (radar sweep, gauge needles, pylon icons) — the
   equivalent of `TargetCam.SetTargetCam()`/`AimCamera` for the TGP feed. Each of radar/gauges/
   pylons likely has its own driving method(s) that would need their own Harmony prefix guard,
   not necessarily the same one.

## Toggle / page selection

Not designed yet. Candidates to evaluate once the above investigation lands:

- A dedicated keybind (`internal-mfd.next`/`internal-mfd.toggle`) on the existing KEY page,
  following the `remote-keybinds`-style precedent of a clearly labelled, off-by-default toggle.
- Reuse of an existing in-cockpit control if the target aircraft already has an idle/unused MFD
  mode button.

Whichever is chosen, restoring the native content cleanly (aircraft change, plugin shutdown, mode
toggled back off) is a hard requirement — same standard `tgp-suppress-native-render.md` already
holds itself to.

## Open questions

- Single canvas/insertion-point assumption: does every playable airframe expose
  `Cockpit.tacScreen.canvas` (or an equivalent) the same way, or does this need per-aircraft
  handling?
- Performance: is native Unity UI redraw of a radar sweep actually cheaper than the status quo, or
  does it just move cost from "duplicate camera render" to "duplicate UI redraw"? No profiling
  done yet.
- Do the driving methods for radar/gauges/pylons need the same "invoke the game's own toggle
  event" treatment `tgp-suppress-native-render.md` landed on (cosmetic-only, camera/renderer
  untouched), or does full content replacement need the stronger Harmony-prefix suppression
  `TargetCamControl` uses?

## Out of scope (for this doc)

- Any actual code, branch, or implementation — this is a feasibility + precedent write-up only.
- Full split-view/paging parity with the external MFD shell inside the cockpit.
- Aircraft other than whatever the investigation phase picks as the first target.
- Changes to `tgp-suppress-native-render.md`'s TGP-specific work — related precedent, separate
  feature.

## Related

- [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md) — same cockpit-MFD problem
  space (native TGP camera), same `SetTargetCam()` reset hazard, currently-shipping cosmetic-only
  answer for that one feature.
- `github.com/9138noms/MFDCustomizer` — proof of the `Cockpit.tacScreen.canvas` overlay insertion
  point.
- `github.com/9138noms/TargetCamControl` — proof of the Harmony-prefix suppression pattern for a
  native driving method.
