# Refactor scan — SRP/DRY review of the repo's 10 largest files

## Status

**All 9 execution-plan steps done and merged to `main`.** See the checked-off
list under "Execution plan" below.

## Where this came from

Two independent architectural scans of the repo's 10 largest files (by line count) were run
separately and are consolidated here into one document. `docs/top-10-refactor-scan.md` was a
broader, principle-level pass; `docs/top-10-refactor-scan-2.md` was a second, independently-produced
pass instructed not to read the first, with every finding checked against the actual code (grep for
call sites, line-by-line comparison where two things "looked" duplicated). Both source docs are
deleted once this one is saved — this file is the merged, current record.

**The file list moved between the two scans.** The original scan's #10 was `src/web/shell/classic/
mfd.css` (625 lines at the time). Today `mfd.css` is 644 lines but `src/web/shell/f35/f35.css` (679
lines) is bigger, so `f35.css` is the accurate #10 — reflected in the table below, which was measured
today (`wc -l`) rather than carried over from either prior pass. `AssetCapture.cs` and `f35.css` were
in the second scan's assigned file list but its actual output only wrote sections for 8 of the 10 files
— both are missing from `top-10-refactor-scan-2.md` entirely, despite that scan's own completion report
claiming all 10 were read. This document adds first-party analysis of both to close that gap, applying
the same framework and verification standard as the rest of this file.

| Lines (today) | File |
| ---: | --- |
| 2,467 | `src/web/shell/classic/mfd.js` |
| 2,092 | `src/plugin/TelemetryServer.cs` |
| 1,373 | `src/plugin/TelemetryReader.cs` |
| 1,250 | `src/web/shell/f35/f35.js` |
| 1,072 | `src/web/pages/map/map.js` |
| 1,003 | `src/plugin/Keybinds.cs` |
| 867 | `tools/serve_web.py` |
| 791 | `docs/layouts.md` |
| 710 | `src/plugin/AssetCapture.cs` |
| 679 | `src/web/shell/f35/f35.css` |

## Review principles

- **SRP** — a file/class/module should have one main reason to change. Mixed concerns (HTTP routing
  + serialization + domain logic + Unity reads, all interleaved) are worth separating; a large file
  that is one legitimately cohesive job is not.
- **DRY** — remove duplicated mechanics and contracts, but don't flatten two things that only *look*
  alike into a misleading shared abstraction. Every finding below was verified against the actual
  code (grepped for other call sites, or read both sides of a claimed duplication side by side) before
  being included — this is the standard this repo's own `docs/build-warning-cleanup.md` and
  `docs/mfd-shell-relay-consolidation.md` already hold themselves to (both of those docs corrected an
  initial assumption after checking more closely; several findings below did the same).
- **Frontend (JS/CSS)** — separate state/model logic from DOM work; isolate browser effects
  (`postMessage`, `fetch`, SSE, canvas, pointer input) at the edges; keep page protocols explicit.
- **Backend (C#)** — separate HTTP transport from routes, routes from domain operations, domain
  operations from Unity/game queries, serialization from state ownership; keep main-thread ownership
  obvious.
- **Docs** — a design/build-log document and a current-state reference document are two different
  jobs; interleaving them means a reader has to filter out superseded prose to find what's true today.

## `src/web/shell/classic/mfd.js` (2,467 lines)

Already the subject of `docs/mfd-shell-relay-consolidation.md`, which found and fixed the
`forward*ToPanes`/`forward*ToFrame` duplication (44 functions collapsed onto two shared helpers,
`forwardToPanes`/`forwardToFrame` at `:588`-`:597`) — visibly in place on disk today. Both scans'
top recommendation for this file (extract relay/payload-builder logic) is therefore already done;
what follows reflects the file's *current* state, not a re-finding of that work.

- **A different, un-consolidated duplication sits one level up, in the `window.addEventListener
  ('message', ...)` dispatcher (`:1553`-`:1775`) — the code that *calls* the now-consolidated
  forwarders.** Eight branches share one exact shape: `<X>Data = m; if (currentPage === '<page>' &&
  !splitMode) forward<X>ToFrame(); if (splitMode) forward<X>ToPanes();` — `tgt` (`:1730`-`:1735`),
  `bdf` (`:1736`-`:1741`), `pal` (`:1742`-`:1746`), `mis` (`:1747`-`:1752`), `obj` (`:1753`-`:1757`),
  `akf` (`:1758`-`:1762`), and `mapinfo`→`wpt` (`:1763`-`:1767`) match verbatim modulo the identifier;
  `rwr`/`mw`/`rdr`/`targets` (`:1710`-`:1729`) are the same shape plus one line of field validation.
  A small table — `{ type, store: setter, page, toFrame, toPanes }` driving one shared branch —
  would collapse these ~40 lines to a handful. Verified this is genuinely uniform, not a repeat of
  the WPN-style trap the relay-consolidation doc already ran into: `loadout`/`cm`/`avn`/`follow`/`grid`
  (`:1611`-`:1704`) are excluded on purpose, since each does real page-specific work (auto-paging,
  dual AVN/AFM forwarding, per-source routing) a generic table entry can't express.
- **The boot-loader/typewriter block (`runBootLoading` `:1798`-`:1814`, `typewriterUrls`
  `:1824`-`:1866`, `setInfoUrls`/`loadConfigUrls` `:1868`-`:1894`) is duplicated across shells.**
  `f35.js`'s `runStripBoot` (`:1052`-`:1067`) and `typeStripUrls` (`:1078`-`:1103`) are, by that
  file's own comment, a "port" of these two functions — structurally identical (same 5%-per-50ms
  fill loop; same done/cursor/rest three-span typewriter). A shared module (e.g. `src/web/shell/
  boot-reveal.js` taking `{ fillEl, doneClassEl, urlSelector }`) would let both shells call one
  implementation. Low risk — neither copy has non-trivial shell-specific behavior once the DOM refs
  are parameterized.
- **`handleLayoutKeydown`/`wireLayoutKeydown` (`:2445`-`:2457`) are byte-for-byte identical to their
  namesakes in `f35.js` (`:1227`-`:1239`)** — verified by direct comparison. Unlike
  `captureLayoutState`/`applyLayoutState`, which genuinely differ (one shell's state is
  `{splitMode, splitVariant, pages, pinnedPage}`, the other's is `{cells, pages}` — real per-layout
  data, not accidental duplication), these two functions and the `open{Save,Load}LayoutModal` pair
  around them contain no layout-specific logic at all, only the literal string `'classic'`/`'f35'`
  and which `*Layouts()` filter to call. A small factory
  (`makeLayoutKeydownHandlers(shellName, captureState, applyState)`) shared by both files would
  remove this exact-duplicate code without touching the genuinely-different state shapes.
- **What's already fine and shouldn't be touched further:** the `mfdButton` switch (`:2119`-`:2252`)
  and its split-mode sibling look like refactor bait by size alone, but every case is a one-line
  dispatch to an existing function or `sendCommand` call — a dispatch table would just move the same
  ~130 lines into object-literal form for no clarity gain. The SOI-cursor block and the split-pane
  geometry functions (`renderSplitLabels`, `mainPaneSlice`, `wpnPaneSlice`, etc.) are bespoke
  per-shell logic already correctly excluded by the relay-consolidation doc's own scope note; reading
  them end to end confirms there's no hidden second copy of this math anywhere else in the file.
- **A broader idea from the original scan, not independently verified here:** a shared
  `src/web/shared/page-protocol.js` declaring page feed names/message-type contracts centrally,
  used by both shells. Plausible and consistent with the item-1 table-drive idea above, but bigger in
  scope (touches both shells' message-type constants, not just the dispatch logic) — worth
  considering once the dispatcher table above exists, not before.

## `src/plugin/TelemetryServer.cs` (2,092 lines)

Both prior scans converge on the same two structural moves, which `docs/server-hardening.md` and
`docs/csharp-unit-testing.md` already scope in detail: extract the JSON-writer layer
(`Serialize`/`*Block`/`*Array`, `:1685`-`:2090`, still accurate — verified today) as `TelemetryJson.cs`,
then split the rest (asset serving, command queue, SSE, MJPEG) into their own files. That plan stands
as written in those two docs; not re-litigated here. **One thing already resolved since the original
scan was written:** it names `EscapeJson` (then a real ~30-line escaper) as part of the file's
serialization responsibility — that extraction already happened during this session's
`docs/csharp-unit-testing.md` work. `TelemetryServer.EscapeJson` (`:2090`) is now a one-line forwarder
to `JsonLite.EscapeJson`. Likewise, `docs/server-hardening.md`'s request-hygiene item (method checks,
body-size caps on `/command`/`/ext/<id>/command`) is also already shipped.

One thing neither prior doc named, found by reading every `Serve*` handler rather than just the ones
already called out:

- **Nine small JSON-response handlers repeat the same six-line response-writing boilerplate.**
  `ServeExtManifest` (`:854`-`:875`), `ServeConfig` (`:989`-`:1005`), `ServeSoiInstances`
  (`:1012`-`:1037`), `ServeKeybindsConfig` (`:1044`-`:1118`), `ServeHudOptions` (`:1126`-`:1139`),
  `ServeWptOptions` (`:1145`-`:1158`), `ServeLayoutOptions` (`:1162`-`:1175`), `ServeHudPresets`
  (`:1180`-`:1193`), `ServeRatesConfig` (`:1199`-`:1214`) each independently do: build a JSON string
  → `StatusCode = 200` → `ContentType = "application/json; charset=utf-8"` → `ContentLength64` →
  `Headers.Add("Cache-Control", "no-cache")` → write → `catch {}` → `finally { Close(); }`. A
  `WriteJson(ctx, string json)` helper would cut each to its one line of actual JSON-building. The
  four binary handlers (`ServeMap` `:1416`-`:1470`, `ServePng` `:1474`-`:1497`, `ServeAirframeImage`
  `:1501`-`:1519`, `ServeAirframeLayout` `:1521`-`:1539`) share the same shape one level down
  (status/content-type/length/write/close, no Cache-Control) — a `WriteBinary(ctx, byte[], string)`
  would do the same. This is orthogonal to the already-planned JSON-writer-layer extraction — that's
  about *what* gets serialized; this is the *HTTP response mechanics* every handler repeats regardless
  of what it's serving, and is still worth doing after `TelemetryJson.cs` lands.
- Everything else — routing (`AcceptLoop`, `:636`-`:749`), the command queue (`:751`-`:850`), SSE
  (`:1587`-`:1681`), MJPEG (`:1541`-`:1585`) — is exactly what `docs/server-hardening.md` already
  describes; re-reading it end to end didn't turn up anything that doc missed.

## `src/plugin/TelemetryReader.cs` (1,373 lines)

The original scan's framing (extract per-feed snapshot builders — `OwnshipSnapshotBuilder`,
`TargetSnapshotBuilder`, `RadarSnapshotBuilder`, etc. — plus a `TelemetryBuildContext` to cut repeated
singleton access) is a bigger, plausible-but-unverified architectural idea; neither scan found
concrete duplication to justify it beyond what's below, and the file's dense builder methods each
project a *different* Unity source into a *different* wire-format field, which is domain complexity
rather than a sign of mixed concerns. Two concrete, verified findings instead:

- **`BuildBdf`/`ClearBdf` (`:374`-`:405`) and `BuildPal`/`ClearPal` (`:412`-`:443`) are the same
  faction-forces block built twice**, differing only in faction name (`FactionHelper.Boscali` vs
  `Primeva`) and which `_bdf*`/`_pal*` fields get written — every line has a 1:1 counterpart,
  including the `Clear*` twins, field-name-for-field-name. A single `FactionForcesBlock
  BuildFactionForces(string factionName)` returning a small struct (assigned at the two call sites in
  `ScanWorld`, `:268`-`:269`) removes this ~70-line, exactly-doubled block. Nothing about BDF vs. PAL
  diverges — both are explicitly documented (`:370`-`:373`, `:407`-`:411`) as the same panel for a
  second, fixed-identity faction, so there's no trap here.
- **`GetSelectedCmCategory` (`:629`-`:660`) duplicates reflection boilerplate with `Keybinds.cs`'s
  `IndexOfCategory` (`Keybinds.cs:978`-`:1001`), in a different file.** Both independently cache a
  `FieldInfo` for `CountermeasureManager`'s private `countermeasureStations` field and a `MethodInfo`
  for `GetFirstCountermeasure` — verified byte-for-byte identical field/method names, just separate
  static caches (`_cmStationsField`/`_cmGetFirstMethod` here vs. `_stationsField`/`_getFirstMethod`
  there). The *logic* on top genuinely differs (this file reads the currently-active station's
  category; `Keybinds.cs` searches for the station index matching a requested category) and
  shouldn't merge — but the reflection access itself could live in one shared
  `CmReflection.GetStations(mgr)` / `CmReflection.GetFirstCountermeasure(station)` pair both files
  call, without touching either file's own read-one-vs-search-many logic.
- The rest of the file — `ScanWorld`/`PushSnapshot`'s large bodies, the RWR/RDR/pitbull builders, the
  reflection-cached getters — is dense but not duplicated: each getter reflects into a *different*
  private field, and `PushSnapshot`'s ~110-line object initializer (`:793`-`:900`) is a flat
  data-assembly step with no branching to extract.

## `src/web/shell/f35/f35.js` (1,250 lines)

Already excluded from `docs/mfd-shell-relay-consolidation.md`'s scope because it independently solved
the same problem differently — a generic `forwardSlice(type)` dispatcher (`:304`-`:318`) over a
`PAGE_FEEDS`/`FEED_AS`/`DERIVED` table (`:66`-`:92`), rather than 44 hand-written functions. Reading
the file confirms that still holds — there is no `forward*ToX` sprawl here to fix.

- The only real finding is the one already covered under `mfd.js` above: the boot-loader/typewriter
  pair (`runStripBoot` `:1052`-`:1067`, `typeStripUrls` `:1078`-`:1103`) and the layout-keydown pair
  (`:1227`-`:1239`) are each duplicated with `mfd.js`'s copies — see that section for the proposal.
- **`placeWpnDecorator` (`:512`-`:530`) is already a single generalized helper**, reused for
  MASTER/MODE/ZOOM/ROUTE/WYPT/RANGE — the same "one function, many callers" shape `mfd.js`'s own
  `placeWpnDecorator` uses, so both shells independently arrived at the same non-duplicated design
  for this specific decorator.
- **Nothing else wants extracting.** `makePortal` (`:264`-`:678`) is long (~400 lines) because a
  portal is a whole independent MFD — page hosting, WPN paging, follow/grid state, SOI cursor
  targets, nav rendering — and every one of those concerns is already a separate named inner function
  called from one `api` object at the end; splitting the closure into multiple files would need to
  thread `frame`/`grid`/`currentPage` through module boundaries for no reduction in what the portal
  actually does. This matches `docs/layouts.md`'s own description of the file (mechanism, with policy
  already split out to `f35-glass.js`/`f35-wpn-paging.js`).

## `src/web/pages/map/map.js` (1,072 lines)

The original scan's proposal to split this into `map-renderer.js`/`map-icons.js`/
`map-interactions.js`/`map-view-state.js`/`map-waypoints.js` reads as a reasonable textbook shape by
file size alone, but doesn't hold up against what's actually in the file: pure geometry is already
delegated to `MapTransform` (`imgRect`/`viewTransform`/`worldToBase`/`overlayToWorld`/`clampPan`,
called through thin wrappers at `:159`-`:170`), waypoint-marker state to `WptRoute` (`:482`, `:491`),
and the telemetry transport to `TelemetrySource` (`:708`) — three of the five proposed modules already
exist as separate files. What's left (canvas drawing, pointer/gesture arbitration, PAD-cursor wiring)
is real, non-duplicated view/interaction logic specific to a canvas-based map that shares tightly
enough state (`view`, `pointers`, `mapMeta`) that splitting it further would mean threading most of
that state back in through a parameter object, for no real reduction in what the file does. Two small,
genuine DRY findings instead:

- **The wheel-zoom handler (`:828`-`:846`) and the pinch-zoom handler (`:874`-`:887`) duplicate the
  same "zoom about a screen point, holding that point's world position fixed, unless following" math.**
  Both compute `z1` clamped to `[MIN_ZOOM, MAX_ZOOM]`, both early-return unchanged if `z1 === z0`,
  both have an identical `if (followPlayer) { view.zoom = z1; ...; return; }` branch, and both apply
  the same reprojection formula (`view.panX = (sx - ox) - (z1/z0) * ((sx-ox) - view.panX)`, and the
  `panY` twin), differing only in how `sx`/`sy` are computed (cursor position vs. pinch midpoint). A
  shared `zoomAbout(sx, sy, z1)` taking the already-clamped target zoom removes ~10 duplicated lines
  from each call site.
- **The click-select flash and waypoint-placement flash (`:618`-`:620`, `:808`-`:810`) are the same
  4-line "set an `{...until}` state, kick off a self-re-arming `requestAnimationFrame` loop that calls
  `drawOverlay()` until expiry" pattern, once each.** Small enough that a shared `makeFlash()` helper
  is a marginal win, not a must-do.

## `src/plugin/Keybinds.cs` (1,003 lines)

The original scan proposes splitting this into `KeybindCatalog`/`KeybindRuntime`/
`JoystickCaptureState`/`TapHoldDetector`/domain-grouped action modules — plausible in the abstract,
but the file's own registry pattern is already doing the job that split would aim for: adding a new
keybind is one `Def`/`DefFree`/`DefKeyOnly`/`AddAxis` call (`:128`-`:330`), not per-bind boilerplate,
and the four small factory functions behind those calls (`:352`-`:416`) are the shared plumbing
already. Reading the whole file:

- **No duplication found beyond the one already noted above** (`IndexOfCategory`, `:978`-`:1001`,
  duplicating reflection boilerplate with `TelemetryReader.cs` — see that file's section; the fix
  belongs to both files equally). `Active`/`JoyBtn`/`ReadAxis` (`:879`-`:936`) each read a genuinely
  different input source (keyboard, joystick button, joystick axis) and don't share enough structure
  to usefully merge; `PollTapHold` (`:777`-`:788`) is already one shared function called for both A/A
  and A/G (`:691`-`:694`), not duplicated per-key logic.
- `docs/build-warning-cleanup.md` already found and removed the dead `AxisValueNow` field this doc's
  predecessor's `:87` reference pointed at — current line 86 is `public bool ActiveNow;`, confirming
  that fix is in place.
- The file is large because it's one long, flat list of bind definitions plus (correctly
  non-duplicated) machinery that drives them — this matches `docs/csharp-unit-testing.md`'s own
  assessment ("low density for its size... clean, separable logic buried in a large bind-table file")
  rather than contradicting it. Splitting the catalog into its own file is a legitimate future move if
  the bind table keeps growing, but isn't justified by duplication today.

## `tools/serve_web.py` (867 lines)

A Python dev-tool HTTP server, not shipped code — evaluated the same lens: does one module handle too
many unrelated concerns, and is there repeated route boilerplate.

- **`H.do_GET` (`:648`-`:775`) is one large if-chain of ~25 routes.** A route table (`{path:
  handler}`) would be more conventional at this size, but is low-priority polish, not a defect — the
  routes are static strings matched top to bottom with no shared setup being skipped.
- **Eight route handlers repeat the same "resolve a manifest key to a captured asset file, or fall
  back" shape**: `/map` (`:680`-`:686`), `/icon` (`:687`-`:694`), `/weapon` (`:695`-`:702`),
  `/tgt-icon`+`/building-icon` (`:703`-`:713`), `/hud-cat-icon` (`:714`-`:724`), `/bdf-icon`
  (`:725`-`:736`), `/tgp.mjpg` (`:737`-`:746`), `/airframe` (`:753`-`:762`) — each independently
  builds a manifest key, calls `_asset_ref`, resolves via `_preview_asset_path`, serves the file if it
  exists, else falls back (placeholder SVG, or 404). A small `_serve_captured(self, key, mime,
  fallback=None)` helper collapses each ~8-line block to one call. Verified identical across all
  eight, down to the `if ref: ... if fp and fp.exists(): return self._file(...)` structure — this is
  the file's single clearest DRY finding.
- **The three stateful mocks — `LAYOUTS`/`_layout_command` (`:310`-`:359`), `PRESETS`/
  `_preset_command` (`:370`-`:401`), `KEYBINDS`/`_keybinds_command` (`:408`-`:627`) — are independent
  state machines bundled into one module**, each with its own module-level mutable state and
  `_command(env)` dispatcher, called from one shared `do_POST` (`:631`-`:646`). They don't share code
  with each other, so this is an SRP observation, not a DRY one — splitting into `tools/preview_mocks/
  {layouts,hud_presets,keybinds}.py` would be straightforward but is genuinely optional for a
  dev-only harness with one maintainer-facing consumer.
- The original scan's broader framing (`mock_config.py`/`asset_manifest.py` module splits) points at
  the same two seams from a different angle — consistent with, not contradicting, the above.

## `docs/layouts.md` (791 lines)

A markdown document, not code — both scans agree the useful lens here is internal duplication,
structure, and clarity, not SRP/DRY literally.

- **Internal duplication, from the doc's habit of layering a correction directly beside the text it
  corrects, rather than updating the reference text and moving the old version to a changelog.**
  Three concrete instances: the "Since then (2026-08-15)" addendum (`:31`-`:46`) restates and
  partially contradicts the Stage 2 summary six lines above it; the `?nochrome` section (`:359`-`:401`)
  carries current behavior, a design-rationale note, and a block explicitly labeled
  "(Historical — kept for the reasoning, superseded by the note above.)" that describes *removed*
  behavior in the present tense until the reader reaches the parenthetical; the gauges section
  (`:596`-`:664`) opens with a note that its own AVN references "now point at deleted code," then
  spends 50 more lines describing those deleted mechanisms in the present tense anyway. The reasoning
  in all three is legitimate and worth keeping, but interleaved with current-state material it means
  a reader has to actively filter out superseded prose.
- **Structural size: the document covers at least four distinct jobs that already have natural seams
  in its own headings** — a stable architecture reference (Terminology/Goal/seam, `:65`-`:241`), a
  staged build narrative with heavy historical detail (`:243`-`:503`), a single subsystem's
  implementation reference (the master strip, `:505`-`:693`), and a running decision log
  (`:695`-`:760`). Splitting along those seams — a `layouts.md` kept to the architecture reference,
  with the stage-by-stage narrative and superseded-but-kept historical asides moved to something like
  `docs/layouts-f35-build-log.md` — matches both scans' recommendation (the original scan additionally
  suggested carving F-35-specific portal/master-strip detail into its own `docs/f35-layout.md`, and
  folding save/load behavior toward the already-existing `docs/layout-save-load.md`, both consistent
  with this split).
- **Not every long passage is a problem.** The portal arrangement rule, the frame-overlap math, and
  "A portal is not the glass" are dense but each describes one specific, still-current mechanism with
  no duplication — candidates to *move* per the structural point above, not to cut or rewrite.

## `src/plugin/AssetCapture.cs` (710 lines)

Not covered by either prior scan's actual output (see "Where this came from"). One-shot game-asset
extraction, already scoped by its own header comment: pull a live Unity `Sprite`/UI widget into
PNG/JPEG bytes once per type/session and hand it to `TelemetryServer`. That scope is mostly honored,
with one real SRP seam and one real DRY finding:

- **`TryCaptureVehicleTypeIcons` (`:494`-`:511`), `TryCaptureShipTypeIcons` (`:521`-`:538`), and
  `TryCaptureBuildingTypeIcons` (`:563`-`:580`) are the same method written three times.** Each walks
  an `Encyclopedia.i.*Types` list, skips entries already in its own `HashSet<string>`
  (`_capturedTgtIcons`/`_capturedBdfShipIcons`/`_capturedBuildingIcons`), and calls
  `SpriteCapture.Request(...typeSprite..., png => TelemetryServer.SetXIcon(name, png))` — differing
  only in which list, which `HashSet`, and which `TelemetryServer` setter. A generic
  `CaptureTypeIcons<T>(IEnumerable<T> list, HashSet<string> captured, Func<T,string> nameOf,
  Func<T,Sprite> spriteOf, Action<string,byte[]> setIcon)` would collapse the three to one
  implementation plus three one-line callers. (`TryCaptureHudCategoryIcons`, `:601`-`:622`, looks
  similar by name but walks a fixed 5-slot array via `transform.Find`, not an `Encyclopedia` list —
  genuinely different shape, correctly left out of this generalization.)
- **A private `EscapeJson` (`:279`-`:291`) duplicates logic this session already extracted for reuse
  elsewhere.** `JsonLite.EscapeJson` (moved out of `TelemetryServer.cs` during this session's
  `docs/csharp-unit-testing.md` work specifically so other files could share it) is a strict superset
  of what this method does — this file's copy only handles `"`, `\`, and control chars via a bare
  `\uXXXX` escape, which is valid JSON but is exactly the subset `JsonLite.EscapeJson` already covers.
  Deleting this private copy and calling `JsonLite.EscapeJson` directly removes a ~13-line duplicate
  with no behavior change.
- **A smaller SRP seam, self-acknowledged by the file's own header comment:** `TryLogPartLayout`
  (`:395`-`:412`) and `TryLogWeaponInfo` (`:426`-`:458`) are one-shot *diagnostic* dumps to the log for
  design/debugging questions — they don't capture any bytes or call into `TelemetryServer` at all,
  unlike every other method in the file. They're a different reason to change (a debugging aid,
  not production asset capture) bundled into an otherwise single-purpose class. Low priority to
  actually move — cheap to leave as-is unless the file is being touched for other reasons — but worth
  naming, since it's the one place the class's own "one-shot extraction" self-description doesn't
  quite fit.

## `src/web/shell/f35/f35.css` (679 lines)

Not covered by either prior scan's actual output (see "Where this came from"). Well-organized with
clear section dividers (Master strip / The tube / Layout picker / Corner grips / Nav labels), uses
shared `--no-*` theme tokens throughout, and explains its few deliberate literal-color exceptions
(`#050a05`, matching AVN's own literal per its comment) rather than leaving them unexplained. One
real, verified DRY finding:

- **`.ms-fll-icon`'s inline SVG mask (`:316`-`:323`) is byte-for-byte identical to `mfd.css`'s
  `.ic-fullscreen` (`mfd.css:210`-`:215`)** — the same `data:image/svg+xml;utf8,...` data URI,
  confirmed by direct comparison, not just the file's own comment claiming "same mask, so both
  shells show the identical icon." A shared token (e.g. a CSS custom property or a small shared
  `icons.css` partial defining `--icon-fullscreen` once in `theme.css`) would let both files reference
  one copy instead of two hand-synced data URIs.
- **What looks like duplication but isn't:** `.ms-tube`/`.ms-fill` (the gauge trough, `:163`-`:200`)
  reads as a copy of `avn.css`'s `.avn-vbar-tube`, and by the file's own comment it deliberately is
  "AVN's gauge tube... turned on its side" — but it's a genuinely different DOM structure (a
  horizontal strip fill vs. AVN's vertical bar with leader-line measurement), not a copy-paste that
  drifted. Correctly left as its own implementation; forcing a shared component here would need to
  parameterize orientation, remeasurement, and the MIL/AB gradient logic for a saving that isn't
  there.

## Priority order

**Quick wins (small, low-risk, clear payoff):**
1. `mfd.js`/`f35.js`: extract `handleLayoutKeydown`/`wireLayoutKeydown` into one shared module —
   exact-duplicate code, no behavior-shape decisions to make.
2. `mfd.js`/`f35.js`: extract the boot-loader/typewriter pair into a shared `boot-reveal.js` —
   already proven identical in shape by `f35.js`'s own "port" comment.
3. `TelemetryServer.cs`: add `WriteJson`/`WriteBinary` helpers for the 9 JSON + 4 binary handlers —
   mechanical, stacks cleanly with the already-planned `TelemetryJson.cs` extraction.
4. `TelemetryReader.cs`: collapse `BuildBdf`/`BuildPal` and their `Clear*` twins into one
   parameterized method + struct — exactly doubled code, no divergence to reconcile.
5. `serve_web.py`: add a `_serve_captured` helper for the 8 manifest-asset routes — mechanical, same
   shape verified across all eight call sites.
6. `AssetCapture.cs`: delete its private `EscapeJson` and call `JsonLite.EscapeJson` instead —
   zero behavior change, removes a ~13-line duplicate of logic already extracted this session for
   exactly this purpose.
7. `AssetCapture.cs`: collapse `TryCaptureVehicleTypeIcons`/`TryCaptureShipTypeIcons`/
   `TryCaptureBuildingTypeIcons` into one generic `CaptureTypeIcons<T>` helper — same shape verified
   across all three.
8. `f35.css`/`mfd.css`: share the fullscreen-icon SVG mask via one token instead of two hand-synced
   copies.

**Needs a little more design work, still worth doing:**
9. `mfd.js`: table-drive the 8-branch `window.message` dispatcher (`tgt`/`bdf`/`pal`/`mis`/`obj`/
   `akf`/`mapinfo`/`rwr` family) — straightforward once the table shape is chosen, but touches a
   dispatcher that's easy to get subtly wrong (see the WPN caveat precedent in the relay-consolidation
   doc), so it deserves its own test-coverage check, not a blind mechanical edit.
10. `TelemetryReader.cs`/`Keybinds.cs`: a shared `CmReflection` helper for the countermeasure-station
    reflection lookup duplicated across the two files — touches reflection into private game fields,
    verify against the in-game checklist both files already point to.
11. `docs/layouts.md`: split the historical/build-log material from the current-state reference
    material (and optionally carve F-35-specific detail into its own doc, per the original scan). Not
    urgent — nothing here is wrong, just harder to navigate than it needs to be.

**Lower priority / skip for now:**
12. `map.js`'s `zoomAbout` and flash-helper extractions — real but small (≤10 lines saved each); fold
    into any unrelated future touch of the gesture code rather than a dedicated pass.
13. `serve_web.py`'s route-table-vs-if-chain and mock-module split — both legitimate, both cosmetic
    for a single-maintainer dev tool with no test coverage of its own.
14. `AssetCapture.cs`'s diagnostic-logging seam (`TryLogPartLayout`/`TryLogWeaponInfo`) — worth
    naming, not worth a dedicated move.

**Considered and set aside — where the two source scans disagreed:**
15. The original scan proposed splitting `Keybinds.cs` into `KeybindCatalog`/`KeybindRuntime`/
    `JoystickCaptureState`/`TapHoldDetector`, and `map.js` into five separate modules
    (`map-renderer.js`/`map-icons.js`/`map-interactions.js`/`map-view-state.js`/`map-waypoints.js`).
    Both are reasonable textbook shapes by file size alone, but neither held up against actually
    reading the files: `Keybinds.cs`'s registry pattern already prevents the duplication that split
    would target, and three of `map.js`'s five proposed modules (`MapTransform`, `WptRoute`,
    `TelemetrySource`) already exist as separate files, with what's left being tightly-coupled
    interaction state that a split would need to thread back together via a parameter object. Not
    recommended unless a future concrete duplication or coupling problem actually shows up in either
    file — file size alone isn't evidence one exists.
16. `TelemetryReader.cs`'s broader per-feed `SnapshotBuilder` split (also from the original scan) is
    the same shape of idea — plausible in the abstract, but neither scan found concrete duplication
    beyond the two items above to justify it, and the file's size is mostly one field-for-field
    wire-format projection per builder, not mixed concerns.
17. `f35.js`, `f35.css`, and the bulk of `map.js`, `Keybinds.cs`, and `TelemetryServer.cs` beyond the
    items above are already well-factored for what they do, confirmed by full reads rather than
    assumed from size.

## Execution plan

Each step below is sized to be one feature branch, per this repo's own git workflow (branch → verify
→ merge on explicit request). Items 15-17 above have no step — they're the two scans' disagreements,
resolved as "don't do this." Say which step number to start; each is self-contained enough to work
from this document alone.

- [x] **Step 1 — shared shell modules (`mfd.js` + `f35.js`)**. Items 1-2. Extract
      `handleLayoutKeydown`/`wireLayoutKeydown` into one shared factory
      (`makeLayoutKeydownHandlers(shellName, captureState, applyState)`) and the boot-loader/
      typewriter pair into a shared `src/web/shell/boot-reveal.js`. Both are exact-duplicate code
      with no behavior-shape decisions to make. Verify: both shells' boot sequence and SAVE/LOAD
      LAYOUT keyboard shortcut still work in `tools/serve_web.py`, classic and F-35.
- [x] **Step 2 — `TelemetryServer.cs` response-writing helpers**. Item 3. Add `WriteJson(ctx, string
      json)` and `WriteBinary(ctx, byte[] body, string contentType)`, and migrate the 9 JSON handlers
      + 4 binary handlers to call them. Mechanical, no behavior change. Verify: `dotnet build` +
      `ci-check.ps1`, then an in-game spot-check of a couple of the migrated endpoints (e.g. `/config`,
      `/map`).
- [x] **Step 3 — `TelemetryReader.cs`: collapse `BuildBdf`/`BuildPal`**. Item 4. One
      `FactionForcesBlock BuildFactionForces(string factionName)` replacing both builders and both
      `Clear*` twins. Verify: `dotnet build`, then in-game BDF and PAL pages both still show correct
      faction data (funds/score/warheads/ship-vehicle-building counts).
- [x] **Step 4 — `serve_web.py`: `_serve_captured` helper**. Item 5. Add the helper and migrate the 8
      manifest-asset routes (`/map`, `/icon`, `/weapon`, `/tgt-icon`, `/building-icon`,
      `/hud-cat-icon`, `/bdf-icon`, `/tgp.mjpg`, `/airframe`) to call it. Verify: `ci-check.ps1`'s
      route smoke, plus a manual check that a couple of these routes still serve real captured assets
      and still fall back correctly when nothing's captured.
- [x] **Step 5 — `AssetCapture.cs` cleanup**. Items 6-7 (optionally 14 in passing, not a dedicated
      move). Delete the private `EscapeJson`, call `JsonLite.EscapeJson` instead; collapse
      `TryCaptureVehicleTypeIcons`/`TryCaptureShipTypeIcons`/`TryCaptureBuildingTypeIcons` into one
      generic `CaptureTypeIcons<T>`. Verify: `dotnet build`, then in-game confirm vehicle/ship/
      building icons still populate on TGT/BDF/HUD pages (these are one-shot per-type captures, so
      check a fresh mission or an unseen unit type).
- [x] **Step 6 — shared fullscreen-icon token (`f35.css` + `mfd.css`)**. Item 8. One shared token
      (CSS custom property, or a small shared `icons.css` partial in `theme.css`) replacing the two
      hand-synced `data:image/svg+xml` copies. Verify: the fullscreen button still renders identically
      in both shells, `tools/serve_web.py`.
- [x] **Step 7 — `mfd.js`: table-drive the message dispatcher**. Item 9. Replace the 8 verbatim
      `<X>Data = m; if (...) forward<X>ToFrame(); if (splitMode) forward<X>ToPanes();` branches with
      one table (`{ type, store, page, toFrame, toPanes }`) and one shared branch. Needs its own
      careful pass (this is the kind of dispatcher the WPN-forwarder caveat already burned once) —
      verify against real data for all 8 covered types (`tgt`/`bdf`/`pal`/`mis`/`obj`/`akf`/`wpt`/
      `rwr`+`mw`+`rdr`+`targets`), both split and full view, live in-game, not just `serve_web.py`.
- [x] **Step 8 — shared `CmReflection` helper (`TelemetryReader.cs` + `Keybinds.cs`)**. Item 10. One
      `CmReflection.GetStations(mgr)` / `CmReflection.GetFirstCountermeasure(station)` pair replacing
      the duplicated `FieldInfo`/`MethodInfo` caches in both files, leaving each file's own
      read-one-vs-search-many logic untouched. Touches reflection into private game fields — verify
      live in-game: countermeasure category display (`TelemetryReader.cs`'s consumer) and the
      keybind-driven CM category cycle (`Keybinds.cs`'s consumer) both still work.
- [x] **Step 9 — `docs/layouts.md` restructure**. Item 11. Split the historical/build-log material
      (Stage 1/2/3 narrative, the superseded-but-kept `?nochrome`/gauges asides) out of the
      current-state reference material, into something like `docs/layouts-f35-build-log.md`.
      Docs-only, no code changes, no build/runtime verification needed — just a careful read-through
      to confirm nothing load-bearing got dropped in the split.

Not planned unless asked (items 12-14, lower priority/skip): `map.js`'s `zoomAbout`/flash-helper
extractions, `serve_web.py`'s route-table/mock-module split, and `AssetCapture.cs`'s
diagnostic-logging seam. All three are real but small enough that folding them into an unrelated
future touch of the same file makes more sense than a dedicated step.
