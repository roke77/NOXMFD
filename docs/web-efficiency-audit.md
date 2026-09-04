# Frontend efficiency audit — `src/web/` hot paths

## Status

An investigation record, not a live status board — the frontend counterpart to
`docs/plugin-efficiency-audit.md`: it records where the web frontend still wastes work, what the
fix for each looks like, and which ones are not worth doing. **For which of the 42 findings below
have actually shipped, see `docs/efficiency-audit-plan.md`** — the findings themselves are left as
originally recorded, not edited in place as work lands.

Audit run against `main` at `92c8783` (version 0.39.1), before the `docs/plugin-efficiency-audit.md`
commit landed.

## Scope and method

All non-test files under `src/web/` (110 files; ~14,600 lines of JS, ~5,100 lines of CSS, ~2,300
lines of HTML) read in full, split across five subsystem passes: the classic bezel shell
(`shell/classic/mfd.js`, the largest single file in the project), the F-35 shell
(`shell/f35/f35.js`), telemetry ingestion and the canvas/SVG display pages (`telemetry-source.js`,
`map.js`, `rdr.js`, `hsd.js`), the interactive pages (`wpt.js`, `sqd.js`, `keybinds.js`, `tgt.js`,
`td.js`), and the remaining ~14 smaller pages. Every candidate was traced to its real trigger
before being recorded — a DOM write only matters once it's known whether it fires ~10 times a
second or once at page load. Eight findings across all five reports were independently
re-verified against the tree by hand; all eight held up exactly as reported.

Findings marked **[verified]** were re-checked directly. The rest rest on a single careful pass.
Confidence is marked per finding:

- **Confirmed** — read directly, no assumption.
- **Likely** — reasoned, with the assumption named.

### Cadence facts the findings depend on

| Path | Rate | Source |
| --- | --- | --- |
| SSE telemetry frame (`/stream`) | ~10 Hz (fast tick), 4 Hz for contact-heavy blocks | `RatesConfig.cs`, confirmed against `telemetry-source.js`'s `_emit()` |
| PAD-cursor / SOI channel | ~60 Hz (rAF-driven while cursor has nonzero velocity) | `pad-cursor.js`'s `drive()` loop; `docs/map-cursor.md` |
| Every page's `postMessage` relay | fires once per matching SSE field, unconditionally, no dedupe in the shared pipeline | `telemetry-source.js`'s `_postUp`, both shells' `RELAY_MESSAGES` tables |
| `window.parent → iframe` forwards | per shell message-listener branch, same ~10 Hz | `mfd.js` / `f35.js` message dispatch |

Every page runs inside an iframe (full-view, or one pane of a 2-4 way split) with no shared
render loop of its own — each page's only entry point is its own `message` listener, so "per
tick" below always means "per relevant `postMessage` the page's listener receives."

## Framing — where this differs from the plugin audit

The plugin audit (`docs/plugin-efficiency-audit.md`) had a hard number to frame itself against:
`docs/performance.md` measured the C# main-thread cost at ~3 ms/sec and called it done. **The web
frontend has no equivalent measured baseline.** `docs/performance.md`'s client-side work is scoped
to exactly one file — `map.js` — and it *was* measured and fixed there (shadowBlur removal,
rAF-coalesced redraw, off-screen culling, all four confirmed still in place below). Every other
page in `src/web/` was never profiled the same way; these findings come from code inspection, the
same as the plugin audit's method, but without a prior "we checked, this is fine" signal to lean
on for anything outside `map.js`.

That shows in the shape of the results. The single biggest pattern here isn't one expensive
call — it's **an idiom the codebase already knows and uses correctly in several places, but
applies inconsistently everywhere else**. `wpn.js`'s `wpnKey`, `tgt.js`'s `builtKey`, `bdf.js`/
`obj.js`'s `sig`, `sqd.js`'s `lastRosterSig`, `hud.js`'s JSON-string compare — all of these
correctly skip a DOM rebuild when the underlying value hasn't changed since the last tick. Ten
other places never picked up the same guard, and rebuild unconditionally at ~10 Hz for values
that hold steady for seconds or minutes at a time (AKF's kill feed, AVN's status tiles, AFM's
failure labels, MIS, MAIN, RWR, and both shells' WPN/TGP nav labels). Tier 1 below is that list.

There is no equivalent of `PerfLog.cs` for the browser side. Chrome/Firefox devtools' own
Performance panel (record a session, look at Scripting/Rendering time and the flame chart under
the `message` listeners) is the right tool if any of this needs measuring before or after a fix —
there's no in-repo instrumentation to restore.

## Priority table

Effort is a rough diff size, not a schedule.

| # | Finding | Cadence | Effort | Confidence |
| --- | --- | --- | --- | --- |
| 01 | HSD rebuilds two fully-static SVG groups every tick | ~10 Hz | 6 lines | Confirmed [verified] |
| 02 | Classic shell: split-mode WPN label rebuild ignores pane content | ~10 Hz | 1 line | Confirmed [verified] |
| 03 | Full-view/master-strip repaint unconditional every tick (both shells) | ~10 Hz | ~25 lines | Confirmed [verified] |
| 04 | AKF page rebuilds entire kill feed + stats every tick | ~10 Hz | 8 lines | Confirmed [verified] |
| 05 | AFM failure-label DOM torn down and rebuilt every tick | ~10 Hz | 4 lines | Confirmed |
| 06 | AVN status tiles repainted unconditionally every tick | ~10 Hz | 8 lines | Confirmed |
| 07 | RDR's scan-limit grid rebuilds every tick off a near-static value | ~10 Hz | 4 lines | Likely |
| 08 | `cursor-zoom.js` rewrites every zoom group's transform every tick | ~10 Hz | 4 lines | Confirmed |
| 09 | MIS and MAIN write the DOM every tick with zero guard | ~10 Hz | 6 lines | Confirmed |
| 10 | `telemetry-source.js` clones the AKF slice every tick regardless of change | ~10 Hz | 6 lines | Confirmed |
| 11 | `placeWpnDecorator` forces a reflow via append→`offsetWidth` (both shells) | per render pass | ~10 lines | Confirmed |
| 12 | `placeOverlayLabel` re-queries the overlay's bounding rect per label | per rebuild | ~10 lines | Confirmed |
| 13 | RDR/HSD recompute the panel viewport twice per ~60 Hz cursor tick | ~60 Hz | ~10 lines/file | Confirmed |
| 14 | WPN's CM readout forces 3 layout reads every tick | ~10 Hz | 5 lines | Likely |
| 15 | Unthrottled full relayout on window resize (both shells) | per resize event | ~6 lines/file | Likely |
| 16 | RDR/HSD never adopted MAP's rAF-coalesced redraw | ~10-60 Hz | ~15 lines/file | Confirmed [verified] |
| 17 | MAP's waypoint marker projection reallocates every redraw | ~10-60 Hz | 8 lines | Confirmed |
| 18 | MAP's grid overlay iterates the full extent, not the viewport | ~10-60 Hz | 10 lines | Confirmed |
| 19 | `telemetry-source.js` double-wraps every outgoing message | ~10 Hz ×15 | 1 line | Confirmed |
| 20 | Classic shell: split-pane AVN geometry recomputed every tick | ~10 Hz | 1 line | Confirmed |
| 21 | F-35: `forwardHsd()` rebuilds/reposts up to 3x per tick | ~10 Hz | 8 lines | Likely |
| 22 | TGT re-queries 6 child nodes per row every tick | ~10 Hz | 10 lines | Confirmed |
| 23 | `wpt.js`'s `renderReadout` computes `findRoute` twice per tick | ~10 Hz | 5 lines | Confirmed |
| 24 | `keybinds.js` rebuilds the whole ~100-row table for one row's change | per click/keypress | ~35 lines | Confirmed |
| 25 | `keybinds.js` polls `/keybinds-config` every 600ms even when embedded | per capture session | 2 lines | Confirmed |
| 26-42 | Dead code and duplication (see that section) | — | ~250 lines net out | mostly Confirmed |

## Tier 1 — the missing render-guard, ten times over

Every finding here is the same shape: a DOM or SVG rebuild that runs on every arriving telemetry
message, with no comparison against what was last rendered, for a value that changes far less
often than the message arrives.

### 01 — HSD rebuilds two fully-static SVG groups every tick [verified]

`pages/hsd/hsd.js:136-146` (`renderGrid`), `:407-415` (`renderOwnship`), both called
unconditionally from `render()` at `:417-426`. ~10 Hz — `render()` runs from the `'hsd'` message
branch, which fires once per real telemetry frame.

`renderGrid()` reads only `CX`/`CY`/`gridFractions()` — all mode-derived, none of them `state`.
`renderOwnship()` reads only `CX`/`CY`. Both rebuild an SVG markup string and reassign
`g.innerHTML` every tick even though the output is byte-identical between ticks unless the pilot
presses MODE, a rare user action.

**Fix:** move both calls from `render()` into `applyMode()` (`hsd.js:37-41`, already called by
`loadRange()` and `toggleMode()`). Nothing in `render()`'s per-tick path needs them.

### 02 — Split-mode WPN label rebuild ignores which pages the panes actually show [verified]

`shell/classic/mfd.js:1906-1911`. ~10 Hz — the `loadout` message is posted unconditionally every
real SSE frame (confirmed in `telemetry-source.js`), and this handler runs on every one of those
while `splitMode` is true, regardless of which two pages are open:

```js
if (splitMode) {
  if (selChanged) autoPageToSelection();
  forwardWpnToPanes();
  renderSplitLabels();
}
```

`renderSplitLabels()` here is unconditional — every sibling handler guards it. The `tgp` handler
two blocks below (`:1934`) does it correctly: `if (panePages.indexOf('tgp') !== -1)
renderSplitLabels();`. This one is the only miss, confirmed by checking all 8 call sites of
`renderSplitLabels()` in the file.

**Fix:** `if (panePages.indexOf('wpn') !== -1) renderSplitLabels();` — one line, mirrors the
existing correct pattern exactly.

### 03 — Full-view WPN/TGP nav labels and the F-35 master strip repaint unconditionally [verified]

Three related sites, same root cause:

- **Classic full view:** `placeWpnNavLabels` (`mfd.js:1077-1092`, called from `:1904`) and
  `placeTgpNavLabels` (`mfd.js:1143-1169`, called from `:1931`) both start by removing and
  recreating every `.overlay-item`/`.wpn-decor` on every `loadout`/`tgp` tick, gated only on
  `currentPage`, never on whether `masterArmsOn`, `combatMode`, page count, or `tgpMarks()` moved.
- **F-35 master strip:** `updateStripFlags` (`f35.js:1229-1234`) and `updateStripGauges`
  (`:1248-1260`) rewrite `className`/`style.width`/`textContent` on 8 flag tiles and 2 gauges on
  every `avn` message — verified directly: no comparison anywhere in either function.

Confirmed by direct read: `updateStripFlags`
```js
function updateStripFlags(m) {
  stripFlags.forEach(function (el) {
    el.classList.remove('on', 'off', 'gear-down');
    el.classList.add(AvnStatusPolicy.tileClass(el.dataset.kind, !!m[el.dataset.field]));
  });
}
```
runs the same 8-tile write whether or not gear/radar/guns/ignition actually flipped since the
last frame — this is the exact shape of `docs/performance.md` item #7, already fixed once for
`HudWaypointCue.LateUpdate` on the C# side, never ported to this file.

**Fix:** cache the last-rendered key tuple per function (`{masterArmsOn, combatMode, maxPage}` for
WPN; `tgpMarks()`'s value for TGP; the 8 booleans + 2 gauge strings for the strip) and skip the
rebuild when unchanged. ~10-15 lines per site, invalidated on page entry so navigating onto the
page still renders immediately.

### 04 — AKF page rebuilds the entire kill feed and stats every tick [verified]

`pages/akf/akf.js:88-92` (`renderFeed`), `:106-122` (`paint`), handler at `:124-128`. ~10 Hz,
confirmed unconditional — no guard exists anywhere in the file before `paint()` runs:

```js
function renderFeed(el, items, renderLine) {
  el.textContent = '';
  for (const e of items) el.appendChild(renderLine(e));
  el.scrollTop = el.scrollHeight;
}
```

The feed is a capped ring buffer of up to 50 lines (`AkfTrackerLogic.cs`'s `MaxFeedLines`), each
line 3-5 DOM nodes — up to ~250 `createElement`/`appendChild` calls per second, sustained for as
long as AKF is on screen, whether or not a kill occurred.

**Fix:** cache a signature of `state.all`/`state.player` (last item id + length is enough) and
skip `renderFeed` when unchanged — the same `sig`/`builtKey` idiom `bdf.js`/`obj.js` already use.

### 05 — AFM failure-label DOM torn down and rebuilt every tick

`pages/afm/afm.js:348-368` (`paintAfmFailures`), called from `:384`. ~10 Hz — the shell re-posts
the map's `avn` frame as `afm` unconditionally whenever AFM is the shown page.

```js
for (const el of afmFailureEls) el.remove();
afmFailureEls = [];
...
```

For any aircraft with an active failure, this destroys and recreates the label element(s) 10×/sec
for as long as the failure lasts, not just once when it starts.

**Fix:** compare the incoming `failures` array against the last-applied one before rebuilding.

### 06 — AVN status tiles repainted unconditionally every tick

`pages/avn/avn.js:65-78` (`setAvnTile`/`paintAvnStatus`), called from `:259`. ~10 Hz. Exactly the
case the original telemetry-reader audit named by title: 8 discrete booleans (gear, radar, guns,
ignition, etc.) that typically hold for many seconds get `classList.remove`/`add` every tick
regardless. `paintAvnGauges` (fuel/rpm/heat/throttle) is correctly *not* flagged — those are
continuously-varying analog readouts, redrawing them every tick is correct.

**Fix:** cache the last-painted 8 booleans, skip `setAvnTile` per-tile when unchanged.

### 07 — RDR's scan-limit grid rebuilds every tick off a near-static value

`pages/rdr/rdr.js:222-235` (`renderGrid`), called unconditionally from `render()` at `:369-381`.
~10 Hz. Output depends only on `coneHalf()` (`state.cone`), which changes far less often than
10 Hz for a given radar mode, but the full 4-line `innerHTML` rebuild runs every tick regardless.

**Fix:** cache `coneHalf()`'s value, skip the rebuild when unchanged. **Likely, not confirmed** —
depends on how rarely radar cone actually changes mid-flight, smaller/lower-confidence win than
finding 01's HSD case.

### 08 — `cursor-zoom.js` rewrites every zoom group's transform every tick, changed or not

`services/cursor-zoom.js:13-22`, called from `rdr.js:380`/`hsd.js:425` at the end of every
`render()`. ~10 Hz. `apply()` does a `getElementById` + `setAttribute('transform', t)` for every
group id (4 on FCR, 6 on HSD) every tick, even when `zoomed` and `t` are unchanged from the
previous call.

**Fix:** track the last-applied `t` string, skip the loop when unchanged.

### 09 — MIS and MAIN write the DOM every tick with no guard at all

`pages/mis/mis.js:24-34` (`paint`), handler at `:36-49`; `pages/main/main.js:33-45`, specifically
`:36-38`. ~10 Hz for both (`mis` posted unconditionally; `status` posted unconditionally from
`telemetry-source.js`'s `_setStatus`). Both are small (4 and 2 writes respectively), but neither
has any guard:

```js
if (m.type === 'status') {
  ibStatus.className = 'ib-status mfd-status ' + m.cls;
  ibStatus.textContent = m.text;
}
```

runs 10×/sec even though the connection status string is essentially constant for the whole
flight. Low magnitude individually — included because it's trivial to fix and representative of
the pattern everywhere else in this tier.

**Fix:** compare `m.cls`/`m.text` (MAIN) and the four MIS fields against last-applied values
before writing.

### 10 — `telemetry-source.js` clones the AKF slice every tick regardless of change

`services/telemetry-source.js:507`: `this._postUp(Object.assign({ type: 'akf' }, d.akf ||
AKF_EMPTY));`, inside `_emit()`, unconditional on every real SSE frame. This is the service-layer
half of finding 04 — even if the *page* is fixed to skip its own rebuild, the shared pipeline still
allocates a new object and structurally clones an ever-growing kill list through `postMessage`,
10×/second, for the whole mission, since kills are sporadic events rather than continuously
varying data.

**Fix:** track a cheap scalar signature (`all.length + '|' + value + '|' + fundsGained + '|' +
fundsSpent`) and skip the `_postUp` call when it matches the last-sent one. Fixing this makes
finding 04's page-side fix redundant in terms of network cost but not DOM cost — do both.

## Tier 2 — forced reflow and redundant layout reads

### 11 — `placeWpnDecorator` forces a synchronous reflow via append-then-read (both shells)

`shell/classic/mfd.js:1119-1128` and `shell/f35/f35.js:637-661` (specifically `:658-660`) —
independently confirmed identical in both files. Runs per call to place a WPN/TGP/MAP decorator
pill (2-3 calls per nav render, amplified by finding 03's missing guards and by every window
resize in the F-35 shell).

```js
overlayEl.appendChild(el);
el.style.right = 'auto';
el.style.left = (centerX - oRect.left - el.offsetWidth / 2) + 'px';
```

Insert, then read `el.offsetWidth` — the classic write-then-read pattern that forces the browser
to synchronously flush layout before it can return a number.

**Fix:** center via CSS transform instead of a JS-measured width — `el.style.left = (centerX -
oRect.left) + 'px'; el.style.transform = 'translateX(-50%)';` — removing the `offsetWidth` read
(and the forced reflow) entirely. ~2 lines per site, same fix in both files.

### 12 — `placeOverlayLabel` re-queries the overlay's bounding rect on every single label

`shell/classic/mfd.js:1604`, inside `placeOverlayLabel` (`:1591-1614`). Placement functions place
5-12 labels per call (`showPage`, `renderSplitLabels`, `placeWpnNavLabels`, `placeTgpNavLabels`),
so `overlayEl.getBoundingClientRect()` is recomputed 5-12 times within a single rebuild pass, even
though `overlayEl` cannot move mid-loop.

**Fix:** compute the rect once at the top of each rebuild function, pass it into
`placeOverlayLabel`/`placeSplitKey`. Touches a widely-called shared helper, so worth a visual
smoke-test of every page's label placement after the change.

### 13 — RDR/HSD recompute the panel viewport twice per ~60 Hz cursor tick

`pages/rdr/rdr.js:110-114` (`viewport`), called from `drawCursor` (`:202`) and `nearestContact`
(`:148`), both from `padMove` (`:213-220`); identical structure in `hsd.js:272-276`/`:357-363`/
`:307-315`/`:370-377`. `padMove` is `pad-cursor.js`'s `onMove` callback, driven by the rAF `drive()`
loop while the cursor has nonzero velocity — confirmed at ~60 Hz by `docs/map-cursor.md`.

```js
function viewport() {
  var p = document.querySelector('.rdr-panel');
  var s = Math.min(p.clientWidth / 520, p.clientHeight / 600);
  return { s: s, ox: (p.clientWidth - 520 * s) / 2, oy: (p.clientHeight - 600 * s) / 2 };
}
```

Two `querySelector` calls plus up to six layout reads compute the identical `{s, ox, oy}` twice
per tick, at 60 times/second while the cursor is being slewed — the one interaction in the app
engineered to run at animation-frame rate.

**Fix:** compute `viewport()` once in `padMove`, pass the result to both callees; cache the panel
element reference once at module load instead of re-querying it every call.

### 14 — WPN's countermeasure readout forces 3 layout reads every tick

`pages/wpn/wpn.js:86-111` (`sizeCm`), called unconditionally from `renderCm` (`:243`), triggered
from the `cm` handler (`:284`), which is posted unconditionally every frame. `sizeCm()` does
`cmPanel.clientWidth`, `cmPanel.getBoundingClientRect()`, and
`cmJammerVal.getBoundingClientRect()` — three forced-layout reads plus follow-up style writes —
every tick, even on ticks where the flare/jammer digit count (the only thing that needs
re-fitting) hasn't changed.

**Fix:** only call `sizeCm()` when the rendered digit-string length actually changes; keep the
bar-fill width/opacity updates (genuinely continuous) unconditional. **Likely** — worst case is a
one-tick-late refit after a digit-count change, invisible in practice, but the assumption is that
digit-count changes are rare relative to the 10 Hz tick — less true mid-firefight.

### 15 — Unthrottled full relayout on window resize (both shells)

`shell/f35/f35.js:1165-1166` and `shell/classic/mfd.js:2693` (identical lack of coalescing in
both, confirmed by direct comparison — this is not a fix ported to one shell and missed in the
other, it's unaddressed in both). Every native `resize`/`orientationchange` event calls
`relayoutAll()`/the equivalent, which for WPN/MAP/TGP/RDR/HSD panes triggers finding 11's
forced-reflow decorator placement, once per fired resize event, with no `requestAnimationFrame`
coalescing — unlike the project's own established pattern for exactly this class of problem
(`docs/performance.md` item #5, MAP's rAF-coalesced telemetry redraw).

**Fix:** wrap the relayout call in a `requestAnimationFrame` coalescing guard in both files.
**Likely**, not confirmed as high-impact — most browsers already throttle native `resize` dispatch
to roughly once per frame, and this is a kiosk/overlay display unlikely to be continuously dragged
during normal play.

## Tier 3 — the rAF-coalescing gap [verified — primary finding, this audit]

### 16 — RDR and HSD never adopted MAP's rAF-coalesced redraw

`pages/rdr/rdr.js:435-471` and `pages/hsd/hsd.js:519-554` — both call `render()` synchronously,
directly inside their `message` event listener:

```js
if (m.type === 'rdr') {
  state = { ... };
  render();
}
```

Verified by reading both message handlers in full: neither file contains `requestAnimationFrame`
anywhere (`grep -n requestAnimationFrame` returns zero hits in both). `docs/performance.md` item #5
fixed exactly this failure mode in `map.js` — bursty SSE-driven redraws weren't coalesced to one
`requestAnimationFrame` callback, so multiple same-frame messages triggered multiple synchronous
redraws — via `map.js`'s `requestDraw()` (`:755-762`). RDR and HSD are both canvas/SVG displays
driven by the identical SSE pipeline and never received the equivalent fix. During the PAD-cursor's
~60 Hz slew (finding 13), both call `render()` on every cursor tick as well, so this compounds with
that finding rather than replacing it.

**Fix:** wrap `render()` in the same `requestDraw()`-style rAF-coalescing pattern `map.js` already
established — a shared helper would let all three canvas/SVG pages call one implementation instead
of `map.js` keeping the only copy. ~15 lines per file, or one shared module plus two call-site
changes.

**Confidence:** Confirmed on the absence of any rAF use in either file (grepped directly). The
severity depends on how often multiple relevant messages genuinely land in the same browser task —
not separately measured here, same caveat `docs/performance.md` itself notes about needing to
measure before optimizing.

## Tier 4 — smaller per-tick allocation and computation waste

### 17 — MAP's waypoint marker projection reallocates every redraw

`pages/map/map.js:517`: `const pts = waypointRoute.waypoints.map(w => Object.assign({}, w,
worldToOverlay(w.x, w.z)));`. Runs on every `drawOverlay()` call — ~10 Hz normally, up to ~60 Hz
during PAD-cursor edge-panning. A fresh array plus one `Object.assign` per waypoint every redraw,
even though `waypointRoute` only changes on the rare `wptroutes:changed` event — only the
projected `cx`/`cy` needs to be live per frame.

**Fix:** reuse a persistent array of `{...w, cx, cy}` objects, mutating `cx`/`cy` in place each
frame; rebuild the array only inside `refreshWaypointRoute()`. Small magnitude (waypoint counts
are typically single digits) — included because it's real per-frame garbage on the one path this
audit specifically checked for regressions against the shipped fixes.

### 18 — MAP's grid overlay iterates the full mission extent, not the visible viewport

`pages/map/map.js:450-506` (`drawGrid`), extent computed from `mapMeta.rw`/`rh` (the full
reachable extent) rather than the current pan/zoom. Runs at 10-60 Hz whenever GRID is toggled on
(off by default). At high zoom (up to 8x), most computed grid lines transform and stroke even
though they land off-canvas — dozens of wasted `stroke()` calls per redraw on a large map, unlike
the contact/missile/waypoint draws which already cull via `onScreen()`.

**Fix:** clamp the iteration range to what's visible at the current pan/zoom (derive from
`overlayToWorld` at the canvas corners) before the loop. Needs care that panning/zooming while
GRID is on doesn't clip lines that should still be visible near the edge.

### 19 — `telemetry-source.js` double-wraps every outgoing message

`services/telemetry-source.js:150-152`:

```js
_postUp(msg) {
  if (window.parent !== window) window.parent.postMessage(Object.assign({ mfd: true }, msg), '*');
}
```

Every call site already builds its own object; `_postUp` allocates a second wrapper just to add
`mfd: true`. ~150 extra small allocations/second across ~15 message types at 10 Hz.

**Fix:** `msg.mfd = true; window.parent.postMessage(msg, '*');` — one line. Smallest, cheapest fix
in the whole report; worth doing alongside finding 10 since they're in the same function.

### 20 — Classic shell: split-pane AVN geometry recomputed every tick, unconditionally

`shell/classic/mfd.js:704-716` (`forwardAvnLayoutToPanes`), called unconditionally from `:1969`
inside the `avn` handler whenever `splitMode` is on. 5 `getBoundingClientRect()` reads every tick
to recompute bezel-key Y-positions that only actually change on resize, split-variant change, or
page entry — all of which already have their own explicit calls to this function (`setSplit`, the
resize handler).

**Fix:** drop the unconditional call from the `avn` tick handler; rely on the existing
resize/`setSplit`/`paneNavigate` call sites. One line removed. Lower cost than finding 11's pattern
since these are pure reads with no write in between, hence its lower placement here.

### 21 — F-35: `forwardHsd()` rebuilds and re-posts the merged payload up to 3x per tick

`shell/f35/f35.js:82` (`PAGE_FEEDS.hsd` lists `hsd`, `mapinfo`, `rdr`, `wpt-routes`), `:389`
(dispatch condition), `:408-417` (`forwardHsd`). If the three underlying slices arrive as separate
typed messages within the same tick — consistent with the message-pump's one-type-per-call design
— a portal showing HSD gets the full recombined object rebuilt and re-posted up to 3 times for
what is conceptually one update.

**Fix:** coalesce via a microtask flag (`if (hsdForwardScheduled) return; hsdForwardScheduled =
true; queueMicrotask(...)`). **Likely**, not confirmed on the exact 3x-per-tick cadence, which
depends on `telemetry-source.js`'s message-batching.

### 22 — TGT re-queries 6 child nodes per row every tick despite already caching row identity

`pages/tgt/tgt.js:174-196` (`renderTargets`). ~10 Hz. The row-identity guard (`targetsKey`)
correctly skips rebuilding rows when the target set is unchanged, but the per-tick update loop
still does 6 `querySelector` calls per existing row every tick:

```js
el.querySelector('.tl-name-text').textContent = ...
el.querySelector('.tl-tti').textContent = ...
el.querySelector('.tl-grid').textContent = ...
```

The same file's own `paint()`/`buildRow()` (lines 71-131) already reuse cached child references
instead of querying — this function just doesn't apply the technique it uses elsewhere.

**Fix:** when a row is created, stash direct references (`row._cells = {name, tti, grid, ...}`)
instead of re-deriving them by class selector every tick.

### 23 — `wpt.js`'s `renderReadout` computes `findRoute` twice per tick

`pages/wpt/wpt.js:415-431`. `renderReadout()` calls `WptRoute.findRoute(c.routes,
c.activeRouteId)` directly, then calls `WptRoute.navigationTarget(c)`, which internally calls the
same `findRoute` again with identical arguments — a duplicate linear scan every ~10 Hz tick.
Negligible impact (route lists are small), a free cleanup rather than a meaningful win.

**Fix:** compute `route` once, pass it into a `navigationTarget` variant that accepts an
already-resolved route.

### 24 — `keybinds.js` rebuilds the entire ~100-row table for a one-row state change

`pages/keybinds/keybinds.js:240-261` (`render()`), triggered from every click/keydown handler in
the file (`keyCellClick`, `joyCellClick`, `axisCellClick`, the `clear`/`invert` handlers, the
capture `keydown` listener). Entering capture mode on one bind re-creates all ~100+ rows and their
buttons, reattaching a fresh `onclick` to each — no delegated listener exists anywhere in the
file.

**Fix:** give each bind a stable row element (`Map<id, rowEl>`, the same pattern `td.js`'s
`leaderRowEls` already uses elsewhere in this codebase), and patch just the changed row instead of
calling `render()`. A single delegated `click` listener on the table container would remove the
need to reattach handlers at all.

### 25 — `keybinds.js` polls `/keybinds-config` every 600ms even when embedded

`pages/keybinds/keybinds.js:371-377` (`updateCaptureFallback`). The comment says this poll exists
"only so standalone KEY previews retain the same feedback path," but the code has no
`window.parent === window` guard — so in the normal embedded case, arming a capture already gets
its completion via the shell's relayed `keybinds-config-push` SSE event, and this timer fires a
redundant `GET /keybinds-config` on top of that for the whole duration a capture is armed.

**Fix:** gate the `setInterval` on a "no shell present" check, matching the file's own stated
intent. **Likely** that the SSE path already covers the embedded case (not directly measured, but
consistent with every other page's pattern in this codebase).

## Correctness issues found in passing

Not performance. Recorded because they surfaced during the read.

- **TD's entire PAD-cursor block is dead code — the crosshair silently never appears on that
  page** [verified]. `pages/td/td.js:347-369` builds a `createPadCursor({onSelect, onMove})` and
  defines the callback functions, but the file's only `message` listener (`:327`) branches solely
  on `m.type`, never on `m.action` — confirmed by grep, zero hits for `.action` anywhere in the
  file. `pad-cursor.js`'s crosshair only shows/drives once `setFocus`/`setVector` are called from
  an `m.action === 'cursor-focus'`/`'cursor'` branch, which `wpt.js:506-508` and `sqd.js:470-472`
  both have and `td.js` is missing — this reads as the wiring being dropped when the block was
  copy-pasted (see finding 33). **Fix:** either delete the ~23 dead lines, or restore the missing
  `message` branches from `wpt.js`/`sqd.js` if PAD-cursor support on TD is actually wanted — that
  choice needs a human decision, not a mechanical fix.
- **`keybinds.js`'s `flashRejected` indexes into the wrong table for Immersion-section binds.**
  `pages/keybinds/keybinds.js:298-308` builds an index `i` into the full `binds` array, but
  `rows = rowsEl.querySelectorAll('.kb-row')` only contains the *main* table's rows — Immersion
  binds render into a separate `immersionRowsEl` (`render():244`). For any Immersion-section bind,
  `rows[i]` refers to the wrong table, so the "flash this row red" rejection feedback silently
  no-ops or highlights the wrong row. **Fix:** the reimplemented-`findIndex` half is mechanical
  (`binds.findIndex(b => b.id === id)`); the table-mismatch needs its own decision about which
  table to search, not folded into a pure simplify pass.

## Tier 5 — dead code and duplication

No meaningful perf claim on most of these; they shrink the code. ~250 lines net removal.

| # | What | Where | Confidence |
| --- | --- | --- | --- |
| 26 | Dead code: `leftKeys`/`rightKeys` aliases (zero references anywhere), the `buildWpnSplitPages` local alias (never called), and the `.ic-split`/`.ic-split::before` CSS ruleset (no class assignment anywhere in `mfd.js`/`mfd.html`) — all confirmed by repo-wide grep, not just local inspection | `shell/classic/mfd.js:21-22`, `:264`; `mfd.css:251-264` | Confirmed |
| 27 | `SPLIT_SLOTS`: six byte-identical 6-slot `[{left×3},{right×3}]` array literals for `akf`/`bdf`/`pal`/`mis`/`obj`/`hsd`, compared character-for-character. One shared `SIX_LR3` constant referenced by all six. Note for whoever does this: nothing mutates these in place today, but a future page-specific edit to one of the six needs to remember they now share a reference | `shell/classic/split-slots.js:51`, `:55-59` | Confirmed |
| 28 | `NAV`'s five "MD family" pages (`akf`/`mis`/`obj`/`bdf`/`pal`) are the same 6-item `[MAIN, AKF, MIS, OBJ, BDF, PAL]` list five times, differing only in which entry carries `mark: true`; the `hud`/`keys` pair repeats the same shape at smaller scale. A small `mdNav(current)` helper generating the list from a `MD_SIBLINGS` array collapses ~40 lines to ~10. Output must stay byte-identical for `nav-model.test.js` | `shell/shared/nav-model.js:83-122`, `:128-139` | Confirmed |
| 29 | Seven near-identical message-builder + forward-pair trios (`tgtMsg`/`forwardTgtToFrame`/`forwardTgtToPanes`, and the same shape for `bdf`/`pal`/`mis`/`obj`/`akf`/`wpt`) — the dispatcher consuming these was already table-driven (`docs/refactor-scan.md` Step 7, confirmed still in place), but the builders it calls were left hand-written, one per type. A factory taking a getter closure (`() => bdfData`) avoids restructuring the existing `let` variables | `shell/classic/mfd.js:846-848`, `:882-907` | Confirmed shape; fix size is Likely |
| 30 | UI-timing constants copy-pasted verbatim across both shells, including identical comments — `COMBAT_MODE_HOLD_MS` (also independently duplicated a third time as `pad-cursor.js`'s `DEFAULT_HOLD_MS`), `TGP_ZOOM_STEP_INITIAL_DELAY_MS`, `TGP_ZOOM_STEP_REPEAT_MS`. Both shells already load several genuinely-shared modules the same way; these three constants were the exception. **[verified]** | `f35.js:184`, `:216-217`; `mfd.js:2597`, `:2655-2656` | Confirmed [verified] |
| 31 | F-35: MAP/TGP/RDR+HSD decorator-placement branches inside `renderNav()` and `resized()` are byte-for-byte identical in both places — diffed directly. WPN's branch stays separate since it calls page-specific work in each caller alongside the shared decorator call | `f35.js:806-817` vs `:871-880` | Confirmed |
| 32 | F-35: `itemsFor()` always `.slice()`-copies the NAV array, but the copy is only needed on the `main` branch (which later `.concat().sort()`s it) — every other page returns an unmutated copy for nothing | `f35.js:551-558` | Confirmed |
| 33 | RDR and HSD duplicate ~150 lines of PAD-cursor hit-test scaffolding (`send`, `itemById`, `viewport`, `scopeRectPx`, `nearestContact`/`nearestContactBy`, `padSelect`/`padDeselect`, `drawCursor`, `padMove`) — logic-for-logic identical apart from DOM-id strings. `docs/page-cursor.md`'s "Round 5" already extracted a smaller, related duplicate (zoom/pending-selection) into `services/cursor-zoom.js`/`services/pending-selection.js`; this larger block was left behind. Same shared-module pattern applies | `pages/rdr/rdr.js:106-220`, `pages/hsd/hsd.js:266-377` | Confirmed |
| 34 | `fmtRng`/`factionClass` duplicated verbatim between `tgt.js` and `td.js` — `td.js`'s own comment says so ("matches tgt.js's own fmtRng") | `tgt.js:135-137`, `:156`; `td.js:54-56`, `:58` | Confirmed |
| 35 | `squadDesignation` duplicated verbatim between `sqd.js` and `td.js`, cross-referenced by comment in both directions; the related "1 = leader, member i → i+2" numbering is also expressed twice (`sqd.js`'s inline `renderSquad` vs. `td.js`'s `squadSlots()`) | `sqd.js:280-282`; `td.js:85-87` | Confirmed |
| 36 | `wpt.js`: `shareRoute`/`shareSteerPoint` are identical bodies (disabled-button guard, optimistic "✓" flip, 1200ms restore) differing only in which `WaypointsStore.share*` method is called. One `shareWithFeedback(promise, btn)` helper | `pages/wpt/wpt.js:551-573` | Confirmed |
| 37 | `wpt.js`: `openExportPanel`/`exportSteerPointsBtn.onclick` duplicate ~8 lines of IO-panel setup (readOnly, error text, button visibility, focus+select), differing only in label and value source | `pages/wpt/wpt.js:364-377`, `:379-393` | Confirmed |
| 38 | `keybinds.js`: `bgInputBtn`'s handler hand-writes the exact shape `makeSettingToggle` was written to factor out — the file's own comment on the factory says "the one above" (this button) inspired it, but it was never migrated onto it | `keybinds.js:58-63` vs `:96-109` | Confirmed |
| 39 | Icon retry-on-404 helper (cap 6 tries, 1200ms delay, cache-bust query param) copy-pasted verbatim 3 times across 2 files. `afm.js` has a related but deliberately more sophisticated version behind its own policy module — not part of this duplication, evidence the codebase already knows how to extract this kind of policy when it matters more | `hud.js:25-39`, `:261-274`; `bdf.js:54-67` | Confirmed |
| 40 | The `{mfd:true, type:...}` postMessage guard (`const m = e.data; if (!m || m.mfd !== true) return;`) retyped verbatim in 11-12 page files instead of one shared `onMfdMessage(fn)` helper. `mapcfg.js`/`tgpcfg.js`/`ext.html` correctly have no such listener at all, since they never receive shell telemetry | `afm.js`, `hud.js`, `tgp.js`, `akf.js` (×2), `wpn.js`, `avn.js`, `bdf.js`, `mis.js`, `obj.js`, `rwr.js`, `main.js` | Confirmed |
| 41 | `remote-keybinds.js`'s `post()` reimplements `services/send-command.js`'s `sendCommand()` — same envelope, same fetch call, independently defined. Fix needs a script-load-order check first: several `.html` files (`afm`, `mis`, `obj`, `rwr`, `bdf`) load `remote-keybinds.js` without ever loading `send-command.js` | `services/remote-keybinds.js:26-33` vs `services/send-command.js:10-16` | Confirmed duplication; fix caveat Likely |
| 42 | The PAD-cursor hover/select adapter (`CURSORABLE`, `createPadCursor({...})`, `padCursorSelectAt`, `hoveredEl`, `padCursorMoveAt`) is copy-pasted verbatim into 3 files — and one of the three copies has already drifted out of sync (see the TD correctness finding above). `tgt.js`'s version is legitimately different (panel-relative rect + hold support) and doesn't need to join this. One factory in `pad-cursor.js`, e.g. `createHoverCursor({cursorable})` | `wpt.js:471-496`, `sqd.js:436-461`, `td.js:348-369` | Confirmed |

## Awareness item, not a bug

`shell/classic/mfd.css:704` carries a single 31,557-character inline base64 image (the decorative
corner-screw photo). It's larger than the rest of the 717-line stylesheet combined, and base64
inflates an already-compressed image ~33% for no caching benefit versus a separate asset request —
but the CSS comment at `:696-700` states this was a deliberate tradeoff ("ships with the page — no
separate asset request, works in-game and in the dev preview alike"). Flagging for awareness, not
proposing to reverse a decision that was already reasoned about in code.

## Not worth chasing

Recorded so a future pass does not re-propose them.

- **All four of MAP's shipped perf fixes are confirmed still in place.** Glow baked once into the
  `tintedIcon` cache (`map.js:233-265`); two-stroke glow (no live `shadowBlur`) in
  `drawTargetBox`/`drawRwrLines`/the waypoint markers; rAF-coalesced redraw via `requestDraw()`
  (`:755-762`); off-screen cull via `onScreen()` for contacts, missiles, waypoints, and steer
  points. `grep -rn shadowBlur src/web/` returns exactly one hit, the one-time glow-bake — no third
  missed spot beyond the two `docs/performance.md` already documents fixing.
- **RWR lines and jam lines correctly have no off-screen cull.** `docs/performance.md`'s shipped
  cull only ever named contacts/missiles/waypoint markers, not lines — a naive endpoint-based cull
  would be wrong for a line (one endpoint off-screen doesn't mean the line isn't visible), and line
  counts are small relative to icon draws. Not worth the added complexity of a correct line-clip
  cull.
- **`telemetry-source.js`'s per-tick derivation of `targets`/`rwr`/`mw`/`rdr`/`pb`/`obj` arrays**
  (new array + `Math.hypot` per row every tick) is correctness-required, not waste — these values
  are genuinely live as units move; the file's own comment on `obj` calls this out explicitly.
- **`pad-cursor.js` already correctly uses `requestAnimationFrame`**, self-stopping when velocity
  reaches zero and only restarting on a nonzero `setVector` call. No leaked timers.
- **`remote-keybinds.js`'s cursor keepalive already batches held directions into one POST per
  50ms tick** — a real asymmetry exists (the fire-state path sends one POST per active group
  instead), but fixing it would need a server-side wire-format change for a case where 1-2
  concurrent held groups is the realistic ceiling. Not included as a primary finding.
- **WPN/TGT/BDF/OBJ/SQD/HUD already correctly implement the sig/builtKey/lastX guard pattern.**
  `wpn.js`'s `wpnKey`/`wpSelIconKey`, `tgt.js`'s `builtKey` (for the expensive rebuild — see finding
  22 for what it *doesn't* cover), `bdf.js`/`obj.js`'s `sig`, `sqd.js`'s `lastRosterSig`/
  `lastSquadRowsSig`, `hud.js`'s JSON-string compare, `keybinds.js`'s `applyConfig`'s `lastJson`
  guard. These are the precedent Tier 1's fixes should be modeled on, not new findings themselves.
- **WPT/SQD/TD's list rendering is already correctly change-gated**, contrary to what the audit
  brief worried it might not be. `wpt.js`'s `tick()` only calls full `render()` on a floating-origin
  change; `sqd.js`'s roster/squad tables bail via content signature before touching the DOM; `td.js`
  deliberately does not mirror the 10 Hz `tgt-targets` feed into its own table, only calling
  `applyLiveTargets()` when the id set changes (a documented design choice so a live-updating table
  doesn't fight with clicking it).
- **AVN's analog gauges** (fuel/rpm/heat/throttle) are continuously-varying flight instruments —
  redrawing them every tick is correct, unlike the discrete status tiles in finding 06.
- **TGP's box renderer and RWR's SVG rebuild per tick are legitimate.** Target-box positions and
  RWR contact bearings are continuously changing telemetry (relative aircraft motion), so full
  per-tick redraw is correct there, unlike AKF/AFM's rarely-changing state.
- **F-35's four portals are not hand-duplicated per index** — all are instances of one
  `makePortal()` factory built in a loop. The "near-identical code repeated 4 times" failure mode
  this audit looked for does not apply; it was already avoided.
- **F-35 has no continuous pointermove-driven portal resizing** — the corner grips are click-only,
  merge/split is a discrete jump between 5 fixed arrangements, not a drag gesture. The "unthrottled
  pointermove resize handler" failure mode doesn't exist here.
- **F-35 portal teardown is clean** — `onGrip`'s merge branch destroys the DOM subtree and drops
  the array reference; no dangling listeners, confirmed by reading `wireLayoutKeydown`'s per-portal
  attachment (re-attaches to each new `contentWindow` on navigation; the old one's listeners die
  with the browser's own iframe-navigation teardown).
- **Classic shell's `RELAY_MESSAGES` table-driven dispatcher, `boot-reveal.js`/`layout-keydown.js`
  sharing, and `forwardToPanes`/`forwardToFrame` consolidation are all confirmed still in place** —
  these are `docs/refactor-scan.md`'s Steps 1 and 7, verified not to have regressed since that scan.
  The `mfdButton()` switch (`mfd.js:2417-2569`) looks like dispatch-table bait by size alone, but
  every case is a one-line call — converting it to an object literal would just move the same lines
  around, per that doc's own conclusion, still valid.
- **No listener/timer leaks found in the classic shell.** Iframe listeners are attached once at
  module load, not per-navigation; every hold-timer (`combatModeHoldTimer`, `wptPrevHoldTimer`,
  `tgpZoomTimer`, `wakeLockErrorTimer`) is tracked and cleared before reassignment.
- **`forwardWptRoutesToPanes()`'s unconditional broadcast to every pane** is deliberate per its own
  comment — routes matter to both MAP and WPT, and it's event-driven off `wptroutes:changed`, not
  the 10 Hz SSE tick. A reasonable, already-justified tradeoff.
- **Grid/list builders that superficially look similar across pages** (`hud.js`'s `subGrid`/
  `catRow`, `bdf.js`'s `buildSection`, `obj.js`'s `buildRows`, `afm.js`'s part/marker builders)
  build structurally different DOM for different purposes — checked and discarded as a duplication
  candidate; a shared "grid builder" would need as much configuration surface as the code it
  replaces.
- **`ext.html` has no `ext.css` and no separate `ext.js`** — confirmed this is by design (the page
  is intentionally minimal), not a missing file.

## Suggested sequencing

Grouped so each step is independently shippable and testable in the `serve_web.py` harness.

1. **Tier 1 (findings 01-10)** as one pass — every one is the identical "add a comparison before
   the write" fix, so doing them together is more efficient than one at a time, and the risk
   profile is uniform (pure skip-paths, no behavior change once the comparison key is right).
   Verify each page still updates on the very next real change, not just that it stops updating on
   an unchanged tick.
2. **16** (rAF-coalescing for RDR/HSD) — pairs naturally with Tier 1 since both touch the same
   `render()` entry points in those two files; do it in the same sitting as 01/07/08 for those
   files specifically.
3. **11, 12, 20** — the forced-reflow and redundant-read cluster in the two shells. Mechanical,
   low-risk, worth a visual smoke-test of every page's label placement afterward since
   `placeOverlayLabel` is widely shared.
4. **26-42** as one deletion/dedup pass, grouped by file so each commit touches a coherent area
   (shells together, RDR+HSD together, the small pages together). Re-run the JS self-checks
   (`nav-model.test.js`, `wpt-route.test.js`, and any page-specific `.test.js`) after 28 and 33
   specifically, since both touch shared data/logic multiple pages depend on.
5. **The two correctness issues** — the TD PAD-cursor question needs a human decision (delete vs.
   restore) before any code changes; `flashRejected`'s table mismatch needs the same.
6. Everything else (13, 14, 15, 17-25) by the priority table, as time allows — all independently
   small and low-risk.

Items needing a decision before implementation: whether TD should support the PAD cursor at all
(correctness section), and whether `remote-keybinds.js`'s `sendCommand` duplication (finding 41)
is worth the `.html` script-order audit its fix requires.

## Review annex — implementation safeguards and plan refinements

Reviewed against `main` at `d26e48c` (version 0.40.0). The findings remain useful candidates, but
the implementation plan needs the corrections and acceptance criteria below before work begins.

### Corrections to proposed implementations

- **Finding 10's proposed AKF signature can suppress real updates.** Once the capped ALL feed has
  50 entries, its length no longer changes. A kill by another player can replace an entry without
  changing the local player's value or funds totals, so `all.length + value + fundsGained +
  fundsSpent` is not a valid identity. Coordinate with plugin finding 26 and publish an explicit
  feed version, incremented at the single `RecordKill` mutation point. Use that version to gate
  transport and feed DOM work; keep the continuously changing rank/fund cards independently
  updateable.
- **Finding 21's `queueMicrotask` does not reliably coalesce `postMessage` events.** Each message
  normally arrives as its own browser task, and the microtask queue drains after each task, so the
  scheduled HSD forward can still run once per message. Use an animation-frame gate, a deliberate
  macrotask boundary, or one canonical slice as the flush trigger. Verify that the final combined
  payload contains the newest HSD, radar, map, and route slices.
- **Finding 19 should be measurement-gated and lower priority.** Mutating the caller-owned object
  in `_postUp` changes the helper's ownership contract to save one small allocation. Keep the
  current copy unless profiling makes this visible; if it is changed, document that callers must
  pass a fresh, unshared object and cover that invariant with a focused test.
- **Finding 27 should avoid a shared mutable slot-array hazard.** If the repeated slot definitions
  are consolidated, freeze the shared structure or return a fresh array from a small factory.
  Referencing one mutable array from six page definitions makes a later page-specific edit affect
  every page silently.
- **Finding 16 does not require a new shared abstraction initially.** Add the small rAF gate locally
  to RDR and HSD unless implementation proves that MAP, RDR, and HSD genuinely share more than the
  scheduling idiom. This keeps page policy beside each page and follows the repository's
  incremental-extraction rule.
- **Finding 40 is not automatically a worthwhile extraction.** The repeated message guard is
  short, explicit, and independently loaded by each page. A shared `onMfdMessage` helper adds
  module/loading coupling and should be adopted only if it also centralizes a real protocol rule
  or prevents demonstrated drift.

### Render-guard requirements

Every new comparison guard must define when its cache is invalidated. At minimum, consider page
entry/navigation, iframe reconstruction, SSE reconnect, mission reset, HSD mode/range changes,
AKF density changes, and any DOM rebuild that replaces the cached nodes. Tests must demonstrate
both halves of the contract: identical input performs no expensive write, and a changed input is
visible on the next eligible render.

The Tier 1 findings should not ship as one cross-frontend pass. Group them into small responsibility
sets, for example:

1. HSD/RDR render guards and rAF scheduling (**01, 07, 08, 13, 16**).
2. Classic/F-35 shell navigation and strip guards (**02, 03**), separately from decorator geometry.
3. AKF transport and page rendering (**04, 10**) together with the plugin's explicit feed version.
4. AFM and AVN guards (**05, 06**) with page-specific tests.
5. MIS/MAIN scalar write guards (**09**) as a small independent cleanup.

Treat the correctness findings as higher priority than deduplication: decide whether TD supports
the PAD cursor and then either restore its missing action branches or remove the dead adapter; fix
KEY rejection feedback so it resolves the row in the main or Immersion table correctly. Record
the decision in the relevant feature document rather than only in this audit.

### Measurement and acceptance matrix

Add a status to every finding: `ready`, `needs measurement`, `needs browser test`, `blocked on
decision`, or `implemented`. Record the affected surface, expected benefit, validation test, and
cache invalidation trigger. Browser profiling should distinguish scripting time, style/layout,
paint, DOM-node churn, and message/structured-clone traffic instead of treating all skipped writes
as equivalent.

Use representative sessions for before/after comparisons:

- classic full view and every classic split arrangement touched by the change;
- all four F-35 portals active, including repeated merge/split and navigation;
- HSD/RDR with a continuously held PAD cursor and bursty telemetry messages;
- AKF with both feeds at the 50-entry cap and kills from local and other players;
- window resize/orientation changes at multiple viewport sizes;
- SSE disconnect/reconnect and mission-present to mission-empty transitions.

Decorator-placement changes (**11, 12**) require visual verification across all pages that use
overlay labels, not only a unit check. rAF/coalescing changes must prove that the last update and
the final cursor position are never dropped.

### Cross-audit dependencies

- Plugin **26** and frontend **04/10** should share one AKF feed-version contract; implementing
  only a client-side approximate signature risks stale data.
- Plugin **13/14** change frontend cold-load behavior and need browser validation for compression,
  conditional requests, and extension assets.
- Plugin **15** changes request scheduling and must be tested with the frontend's concurrent asset,
  SSE, MJPEG, and command traffic before either audit calls the HTTP work complete.
