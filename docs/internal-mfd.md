# Internal MFD — native cockpit rendering of NOXMFD pages

## Status

**Proof-of-concept live-verified on one aircraft (T/A-30 Compass)**, `feature/internal-mfd-poc`.
Code lives in `src/plugin/InternalMFD/` — see [Code organization](#code-organization) for the
per-file split. The T/A-30's center screen splits into two panes (see
[Split-screen layout](#split-screen-layout)): the right pane is a live, source-matched **RWR**
scope (`InternalMfdRwrPage.cs` — concentric range rings, contact blips, inbound-missile bearing
indicators, all confirmed live against the real page's own SVG/JS values); the left pane switches
between two pages rather than holding one fixed page (see
[Left-pane TGP override](#left-pane-tgp-override) below).

Live-verified: the TGP camera feed's overlay (RNG/ALT/MODE/...) shows up automatically —
`cam.targetTexture` (what `InternalMfdTgpPage.cs`'s `RawImage` reads) turned out to already carry
the native overlay baked in, not just the raw picture, so no separate overlay port was needed.

AVN (speed/altitude/fuel) was implemented, live-verified, and then removed — real visual parity
with the web AVN page (icon tiles, tick-ring dial gauges) was judged too large a job to chase
incrementally; RWR was picked instead as a smaller, single-page target to prove native content can
match a real page closely (see [Live findings](#live-findings)).

Not yet done: live verification of the restore-cleanly paths (toggle-off is exercised every test
session; aircraft-change and mission-exit are coded but not explicitly confirmed live).

## Left-pane TGP override

The left pane isn't a single fixed page. **HSD** (`InternalMfdHsdPage.cs`) mounts there by
default; **TGP** (`InternalMfdTgpPage.cs`) takes over the instant the player has a real weapon lock
or `TgpManualControl.ManualMode` is on (`InternalMfdController.IsTgpActive()` — the same
hasTargets/ManualMode check `TgpFeed.CaptureFrame`/`TgpFullScreen.Tick` already use, so this can't
disagree with whether the TGP feed itself is actually live), and HSD remounts the moment neither is
true. Both pages are built once at overlay-construction time and kept alive behind their own
wrapper `GameObject`; the swap is a `SetActive` toggle on whichever wrapper, checked at the
controller's normal 10Hz page-refresh cadence — not a rebuild, so neither page loses its pooled UI
state (contact markers, etc.) across a swap.

HSD itself (`InternalMfdHsdPage.cs`) is a deliberately simplified read of the real page
(`src/web/pages/hsd/hsd.js`): the grid rings (theme.css `--no-hsd-pink-rgb`), the notched
contact/ownship icon (`hsd.js`'s own `'M0 -9 L-6 7 L0 4 L6 7 Z'` polygon, filled here rather than
RWR's stroke-only caret), contact colors (`--no-purple` datalink / `--no-red` own-radar /
`--no-white` stale / `--no-amber` focused-lock), and the AA threat rings (`--no-hsd-yellow`) are
matched to source. CEN/DEP mode and the selected range track the external web HSD page live (see
[HSD view sync](#hsd-view-sync) below) — everything else needing a cursor/bezel input this pane
doesn't have wired up yet is still simplified out: no radar-cone overlay, no active-route line, and
no PAD acquisition cursor (contacts can't be selected from this pane); see the file's own header
comment for the upgrade path on each.

## HSD view sync

`InternalMfdHsdPage.cs`'s CEN/DEP mode and selected range track whatever the external web HSD page
is currently set to, rather than a fixed view — changing range or toggling DEP on the web page
updates the internal pane within one refresh tick (~100ms). One-directional (web → internal only;
opening a second web HSD tab doesn't itself converge with a first, since hsd.js's own state is
still per-tab `sessionStorage`) because that's the sync this was asked for — the plumbing:

- `HsdViewState.cs` (`src/plugin/`, not `InternalMFD/` — it's shared game state, not internal-MFD-
  specific code) holds the current `Dep`/`RangeIdx`, in-memory only (matching `sessionStorage`'s own
  per-session scope, nothing to persist across a game restart either).
- `hsd.js`'s `saveRange()` — the one function both `setRangeIdx()` (range step) and `toggleMode()`
  (CEN/DEP toggle) already call — now also sends `hsd.set-view` (`{on: dep, index: rangeIdx}`,
  reusing existing envelope fields rather than adding new ones) alongside its existing
  `sessionStorage` write.
- `CommandDispatcher`'s `hsd.set-view` handler calls `HsdViewState.Set`.
- `TelemetryReader` copies `HsdViewState.Dep`/`RangeIdx` into `TelemetrySnapshot.HsdDep`/
  `HsdRangeIdx` every tick, the same snapshot `InternalMfdController` already passes into
  `InternalMfdHsdPage.Refresh`.
- `InternalMfdHsdPage.Refresh` computes the actual range (its own `CenRangeNm`/`DepRangeNm` ladders,
  matching `hsd.js`'s `CEN_RANGE_NM`/`DEP_RANGE_NM`) and DEP's own geometry (ownship pushed toward
  the bottom, a bigger ring that runs past the pane's own edges — `hsd.js`'s
  `CEN_CY`/`CEN_OUTER`/`DEP_CY`/`DEP_OUTER`, expressed as fractions of this page's own half-width) —
  a `RectMask2D` on the scope container reproduces the SVG viewBox's own implicit clipping for the
  part of a DEP-mode ring that runs past the visible square.

## Code organization

`src/plugin/InternalMFD/`, split by responsibility rather than kept as one growing file:

- **`InternalMfdController.cs`** — the `MonoBehaviour`: toggle, canvas-resolution dispatch, split-vs-single
  layout construction, per-frame `Refresh` dispatch to whichever pages are mounted. Doesn't know
  what a page actually draws.
- **`InternalMfdScreenResolver.cs`** — finds the local player's `Cockpit`/`TacScreen`/`Canvas` for a
  given aircraft (the reflection chain, the T/A-30 UV crop constant, attach diagnostics). A distinct
  concern from what gets drawn once the canvas is found.
- **`IInternalMfdPage.cs`** — the interface every page's content implements: `Refresh
  (TelemetrySnapshot)` only. Construction is deliberately not part of the interface — each page
  type takes whatever constructor parameters it needs, which the controller already knows at the
  call site.
- **`InternalMfdUi.cs`** — small UI-construction primitives (`NewUi`, `ResolveFont`, `Stretch`) and
  procedural-sprite/geometry helpers (`ResolveRingSprite`, `EdgeSigned`, `DistancePointSegment`,
  `Azimuth`, `PolarToLocal`) shared by every page, instead of duplicated per page (all five moved
  here from `InternalMfdRwrPage.cs` once `InternalMfdHsdPage.cs` needed the same ring-drawing,
  polygon-rasterizing, and contact-placement code a second time).
- **`InternalMfdScopeMath.cs`** — the pure sin/cos placement math `InternalMfdUi.PolarToLocal` wraps
  (`PolarOffset`) plus a zero-padded heading formatter (`Pad3Heading`), BCL-only so it can be linked
  into `tools/tests` without dragging in UnityEngine — the same split `Tgp/TgpFullScreenMath.cs`/
  `TgpManualAimMath.cs` use for their own geometry.
- **`InternalMfdHsdPage.cs`**, **`InternalMfdRwrPage.cs`**, **`InternalMfdTgpPage.cs`** — one file
  per page, each implementing `IInternalMfdPage`. A new page is a new `InternalMfd<Name>Page.cs`,
  not a growing switch statement in the controller.

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

The insertion point is `Cockpit.tacScreen.canvas` — both private fields, reflected the same way
`tgp-suppress-native-render.md` already reflects `TargetCam`'s fields. `Cockpit` itself has to be
found by scanning every live `Cockpit` component for the one whose own private `aircraft` field
matches the local player's `Aircraft` (`GameManager.GetLocalAircraft`) — it does **not** sit on
`aircraft.cockpit` (that's a different GameObject, the structural/damage `UnitPart` `EscapeCapsule`
happens to sit on) or anywhere under `Aircraft`'s own transform hierarchy; both were tried live and
found nothing. A resolved `Cockpit`'s `tacScreen` field is only non-null for the local player's own
aircraft (`Cockpit_OnAircraftInitialize` sets `base.enabled` accordingly for everyone else too —
`isActiveAndEnabled` alone is a simpler equivalent check, per an existing third-party mod, see
[External precedent](#external-precedent)).

Insert a `GameObject` as a child of that `Canvas`, last-sibling so it paints on top:

```csharp
var overlay = new GameObject("MyOverlay", typeof(RectTransform));
overlay.layer = canvas.gameObject.layer; // SetParent does NOT inherit the parent's layer
var rt = overlay.GetComponent<RectTransform>();
rt.SetParent(canvas.transform, false);
rt.SetAsLastSibling();
```

**The canvas is shared by every physical screen in the cockpit, not just one.** For the T/A-30
Compass, `TacScreen`'s `Canvas`/`Camera`/`RenderTexture` (1024×512) back a single mesh/Renderer/
material (`cockpit_F/cockpit_F_int/tacscreen`, confirmed live: exactly one `Renderer` in the scene
references the render texture, via its material's `_EmissionMap` slot, not the default `mainTexture`
slot `Material.mainTexture` reads) covering all three cockpit screens as different vertical bands of
that one texture, cropped via the mesh's own per-vertex UVs — not a material-level transform
(`Renderer.sharedMaterial.mainTextureScale`/`mainTextureOffset` read back as the trivial
`(1,1)`/`(0,0)`, which only means no *additional* transform on top of the mesh's own baked UVs, not
"uncropped"). A full-canvas overlay therefore paints on **every** screen at once. Anchoring the
overlay's `RectTransform` to just the target screen's own UV band fixes this:

```csharp
rt.anchorMin = new Vector2(0f, 0.29068f); // T/A-30 Compass center screen only
rt.anchorMax = Vector2.one;
```

Confirmed live. The exact band is per-aircraft mesh data (see
[Per-aircraft screen geometry](#per-aircraft-screen-geometry-open)) — camera culling masks and
canvas render mode were tried first and don't isolate anything, since there's only one Renderer to
begin with; see [Live findings](#live-findings) for the dead ends.

If replacement also requires suppressing native drive logic (radar sweep, gauge needles), use
guarded Harmony **prefix** patches on the relevant per-frame methods, following
[`tgp-suppress-native-render.md`](tgp-suppress-native-render.md)'s precedent — not yet needed for
the current placeholder-only POC, which draws on top rather than replacing what drives the content
underneath.

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
| Radar picture | `RDR` page | Sweep/contact rendering — RWR (a related but simpler radar-warning page, not the main `RDR` radar picture) is the current live page — see [Status](#status) |
| Targeting pod feed | `TGP` page | Camera feed — live, see [Status](#status); text/status overlay not yet ported |
| Pylon/loadout display | `WPN` page | Icon-per-station grid, already fairly static-shaped |

AVN (aircraft gauges: speed/altitude/fuel) was implemented and live-verified, then removed —
matching the web AVN page's icon tiles and tick-ring dial gauges closely enough to be worth
keeping was judged a larger job than the POC's next step warranted; RWR was picked instead as a
smaller target to prove native content could match a real page closely, and did.

Scope for a first pass: **one** page at a time replacing the cockpit MFD content, with some way
to cycle which one is shown (see [Toggle / page selection](#toggle--page-selection)). Standing
requirement as of this aircraft's cross-check (see [Split-screen layout](#split-screen-layout)):
wide screens split into two independently-addressable halves; this is not full split-view/paging
shell parity with the external MFD (page cycling, N-way splits, drag-to-resize), just a two-region
layout on the physical screens shaped for it.

## Investigation needed before implementation

1. **What `Cockpit.tacScreen.canvas` actually contains** — resolved, live-confirmed: see
   [Feasibility approach](#feasibility-approach) and [Live findings](#live-findings). The remaining
   piece is per-airframe geometry, tracked separately below.
2. **What drives the *other* cockpit MFD content** (radar sweep, gauge needles, pylon icons) — the
   equivalent of `TargetCam.SetTargetCam()`/`AimCamera` for the TGP feed. Each of radar/gauges/
   pylons likely has its own driving method(s) that would need their own Harmony prefix guard,
   not necessarily the same one. Still open — out of scope for the current placeholder-only POC,
   which covers its content rather than suppressing what drives it.

## Per-aircraft screen geometry (open)

The T/A-30 Compass's three-screens-one-mesh layout, and its exact UV bands, are one aircraft's data
— confirmed correct for that aircraft only (cross-checked two ways, see
[Live findings](#live-findings)), not assumed to generalize. Before a real (non-placeholder) page
targets a second aircraft, its own screen(s) need the same treatment: identify the mesh/Renderer,
confirm whether it's shared across multiple physical screens or not, and get that aircraft's own UV
band(s). Two ways to get there without repeating this session's live trial-and-error, both
third-party prior art (not yet pulled into this repo — see [External precedent](#external-precedent)
for licensing/attribution before reusing either):

- A hand-measured lookup table, if another mod has already measured the target aircraft.
- A runtime tool that computes UV islands directly from the mesh's own vertex data (GPU readback +
  union-find clustering on shared UV edges) — no hand-measurement needed, works on any aircraft.

## Split-screen layout

**Standing requirement, established after the T/A-30 POC:** a wide physical screen splits into two
independently-addressable halves with a vertical separator; a square-ish screen stays one full-view
region. Per-aircraft, following the same reasoning as
[Per-aircraft screen geometry](#per-aircraft-screen-geometry-open) — the physical screen's own
proportions decide this, not a global setting.

The T/A-30's own center screen (the only screen measured so far) is a data point for this rule: its
UV band is roughly 1024×364 px within the shared texture (see
[Feasibility approach](#feasibility-approach)), a ≈2.8:1 aspect ratio — wide, and implemented as a
split: `InternalMfdController.cs` builds a left and right `RectTransform` half with a vertical separator
between them, and mounts `IInternalMfdPage`s into each half — the right half holds
`InternalMfdRwrPage` (fixed); the left half holds both `InternalMfdHsdPage` and `InternalMfdTgpPage`
and switches between them (see [Left-pane TGP override](#left-pane-tgp-override) and
[Status](#status)).

Open, not yet decided:

- Exact threshold (or per-aircraft judgment call) for "wide" vs. "square-ish" — no numeric aspect
  ratio picked yet.
- Whether the right half's fixed RWR should also become player-selectable, matching the left half's
  now-dynamic content.
- How the separator itself is drawn (a thin native `Image` divider vs. just the gap between two
  independently-anchored regions).
- Whether "split" reuses any of the external shell's existing split-view concepts
  (`split-slots.js`, `mfd-split-routing.test.js`) or is a from-scratch native layout — the external
  shell's split-view is DOM/CSS-driven and doesn't carry over mechanically, but the *page selection
  per pane* concept might.

## Toggle / page selection

Implemented for the POC: `internal-mfd-poc-toggle` (`Keybinds.cs`, `DefFree`, off-by-default,
unbound until set on `/keybinds`), with a matching `/command` case
(`internal-mfd.poc-toggle`) so a remote keybind press also works. Restoring native content is
handled for toggle-off, aircraft change (an aircraft-identity check, not just a fake-null check on
the cached `Canvas`), and mission exit (the static enabled flag resets in `OnDestroy`, which fires
when `MissionLifecycle` tears down the mission-scoped reader). Not yet handled: any restore-on-
init-failure case beyond "the overlay object is simply never cached and the next frame retries."

## Live findings

Debugging the T/A-30 POC live went through three wrong theories before the real mechanism, each
disproven with actual log/diagnostic evidence rather than guessed away — kept here as the record of
what doesn't work and why, so it isn't retried:

- **A dedicated overlay layer, added to `screenCam`'s culling mask only while attached.** Theory:
  `TacScreen.canvas`'s `GameObject.layer` is Unity's built-in, project-wide-default "UI" layer
  (layer 5) — plausible that other screens' cameras also include it by plain convention, not
  anything specific to this one screen. Live result: no change. Disproven for a simpler reason found
  afterward — there's only **one** camera involved at all (`screenCam`), so a second camera was
  never the mechanism to isolate against.
- **Cropping to the center screen's own material UV transform.** `Cockpit.tacScreenRender`'s
  material reported `mainTextureScale`/`mainTextureOffset` of the trivial `(1,1)`/`(0,0)`, read as
  "this mesh shows the full, uncropped texture." Live result: no change, because the premise was
  wrong — trivial scale/offset means no *additional* uniform transform on top of the mesh's own
  per-vertex UVs, not "no cropping." The actual crop lives one layer deeper, baked into the mesh
  itself, which `Material.mainTextureScale`/`Offset` can't see at all.
- **Assuming multiple Renderers share the RenderTexture.** A scan of every `Renderer` in the scene
  for one referencing `TacScreen.renderTexture` via `.mainTexture` came back with **zero** matches —
  including on `tacScreenRender` itself, the one Renderer already known to display it. Cause: this
  is an emissive screen shader (`TacScreen.Update()` sets `_EmissionColor` on it every frame), so
  the render texture is bound to an emission texture slot, not the shader's default/albedo slot
  `Material.mainTexture` reads. Widening the scan to every texture property the material actually
  has (`Material.GetTexturePropertyNames()`) found exactly **one** match, via `_EmissionMap` —
  confirming all three screens really are one Renderer, one material, one mesh, and settling why
  layer/camera isolation was never going to work.

The eventual fix (crop the overlay to the center screen's own UV band within that one shared
texture) was reached by a different route than the above: a review inspected the T/A-30's actual
mesh/material/UV data directly (not decompiled C#, actual asset data) and reported the center
screen's band as V ≈ 0.29068–1.0 (full width). That number was cross-checked against `MFDCustomizer`
(see [External precedent](#external-precedent)), an unrelated third-party mod with its own
independently hand-measured layout table that includes this exact aircraft — its measurement for the
same screen converts to V ≈ 0.299–0.994, matching to within rounding. Applied and confirmed live:
the overlay now shows only on the center screen, not the two smaller side screens sharing the canvas.

## Open questions

- Performance: is native Unity UI redraw of a radar sweep actually cheaper than the status quo, or
  does it just move cost from "duplicate camera render" to "duplicate UI redraw"? No profiling
  done yet.
- Do the driving methods for radar/gauges/pylons need the same "invoke the game's own toggle
  event" treatment `tgp-suppress-native-render.md` uses (cosmetic-only, camera/renderer untouched),
  or does full content replacement need guarded Harmony-prefix suppression?
- See [Per-aircraft screen geometry](#per-aircraft-screen-geometry-open) for the still-open
  per-airframe question.

## External precedent

Four existing third-party BepInEx mods manipulate this same cockpit surface, found mid-investigation
(not previously known to this repo). Not depended on or pulled into this codebase — noted here as
prior art, since re-deriving what they've already solved would be wasted effort if this feature
grows further:

- **[MFDCustomizer](https://github.com/9138noms/MFDCustomizer)** — independently arrived at the same
  `Cockpit.tacScreen`/`TacScreen.canvas` reflection chain this doc uses, plus a simpler local-player
  check (`Behaviour.isActiveAndEnabled`, since `Cockpit_OnAircraftInitialize` only enables the local
  player's own instance — simpler than this POC's "match every `Cockpit`'s own `aircraft` field"
  scan). Ships a hand-measured, per-aircraft, per-*slot* (multiple named screens, e.g. `main`,
  `panel`, `AoA`, `engine`) pixel-rect table covering over a dozen aircraft, T/A-30 Compass
  included — the source of this doc's cross-check above. Confirms the render texture is 1024×512 for
  every aircraft, not just the T/A-30.
- **[3DWebviewLoader](https://github.com/Assassin1076/3DWebviewLoader)** — takes a different
  insertion approach (swaps the target Renderer's material outright to show a Vuplex webview
  texture, rather than inserting into the existing canvas). Ships a genuinely reusable diagnostic:
  `RuntimeMeshExtractor` reads a mesh's GPU vertex/index buffers back asynchronously and clusters
  triangles into UV islands (union-find over shared UV edges), exporting a bounding box per island
  plus a labeled preview PNG — an automated alternative to hand-measuring a new aircraft's screen
  regions, used there as a developer-facing tool rather than at runtime for automatic per-screen
  targeting.
- **[NuclearOption-MFDBlockBlast](https://github.com/9138noms/NuclearOption-MFDBlockBlast)** and
  **[NuclearOption-MFDVideoPlayer](https://github.com/9138noms/NuclearOption-MFDVideoPlayer)** — same
  problem space (content in the cockpit MFD); not inspected in depth.

Check each repo's license before reusing any code or measured data directly.

## Out of scope (for this doc)

- Full split-view/paging parity with the external MFD shell inside the cockpit.
- Aircraft other than whatever the POC confirms works cleanly.
- Changes to `tgp-suppress-native-render.md`'s TGP-specific work — related precedent, separate
  feature.

## Related

- [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md) — same cockpit-MFD problem
  space (native TGP camera), same `SetTargetCam()` reset hazard, currently-shipping cosmetic-only
  answer for that one feature.
