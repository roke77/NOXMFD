// Generate the line-select keys around the screen (easy to tune).
const COUNTS = { 'keys-left': 6, 'keys-right': 6, 'keys-top': 4, 'keys-bottom': 4 };
function addSep(c) { const s = document.createElement('div'); s.className = 'sep'; c.appendChild(s); }
function addKey(c) { const b = document.createElement('button'); b.className = 'key'; b.type = 'button'; c.appendChild(b); }

// Pattern: ridge, key, ridge, key, … ridge — separators top & bottom so keys sit centered.
for (const id in COUNTS) {
  const container = document.getElementById(id);
  addSep(container);
  for (let i = 0; i < COUNTS[id]; i++) {
    addKey(container);
    addSep(container);
  }
}

const keyBanks = {
  left:   document.querySelectorAll('#keys-left .key'),
  right:  document.querySelectorAll('#keys-right .key'),
  top:    document.querySelectorAll('#keys-top .key'),
  bottom: document.querySelectorAll('#keys-bottom .key'),
};
const leftKeys  = keyBanks.left;    // compatibility aliases for side-specific renderers
const rightKeys = keyBanks.right;
// Name every key by its bank+index. placeOverlayLabel stamps the same string on the label it puts
// there, which is what lets the SOI cursor go from a physical key to the label riding on it.
['left', 'right', 'top', 'bottom'].forEach(function(side) {
  keyBanks[side].forEach(function(k, i) { k.dataset.pos = side + i; });
});
// Fixed-control icon banks. The top row holds page-independent functions; the bottom row
// holds layout controls. Both are wired once at startup and excluded from clearKeyActions,
// so they survive page switches.
const layoutIcons = [
  { cls: 'ic-square', title: 'Full view',            action: 'unsplit' },
  { cls: 'ic-2x1',    title: 'Split top/bottom',     action: 'split'   },   // H_SPLIT
  { cls: 'ic-1x2',    title: 'Split left/right',     action: 'vsplit'  },   // V_SPLIT (50/50)
  { cls: 'ic-lr23',   title: 'Split left/right 2:1', action: 'vwsplit' },   // V_WIDE_SPLIT (2:1)
];
const functionIcons = [
  { cls: 'ic-hide-shell', title: 'Hide shell', action: 'hide-shell' },
  { cls: 'ic-fullscreen', title: 'Fullscreen', action: 'fll' },
  { cls: 'ic-pin',        title: 'Pin',        action: 'pin' },
  { cls: 'ic-swap',       title: 'Swap',       action: 'swap' },
];
function applyIconBank(bankName, icons) {
  icons.forEach(function(icon, i) {
    const key = keyBanks[bankName][i];
    if (!key) return;
    key.classList.add('icon');
    key.title = icon.title;
    if (icon.action) key.dataset.action = icon.action;
    const span = document.createElement('span');
    span.className = icon.cls;
    span.setAttribute('aria-hidden', 'true');
    key.appendChild(span);
  });
}
applyIconBank('top', functionIcons);
applyIconBank('bottom', layoutIcons);

// Remember the chosen layout so a fresh load honors it (docs/layouts.md, Stage 3). The head guard
// in each shell's HTML reads this value and redirects before paint. Guarded: localStorage throws
// in some private-mode browsers, and a failed write just means the choice isn't sticky.
function setLayout(name) { try { localStorage.setItem('layout', name); } catch (e) {} }
const overlayEl = document.getElementById('overlay');
const mapFrame  = document.querySelector('.screen > iframe[title="map"]');
const screenEl  = document.getElementById('screen');
const paneIframes = [document.getElementById('pane-top'), document.getElementById('pane-bot')];
const soiRingEl = document.getElementById('soi-ring');
const pageFrame = document.getElementById('page-frame');   // full-view host for the frame-hosted pages (WPN, TGT, TGP)
// Pages that render in #page-frame in full view (rather than as overlay renderers); showPage
// switches the frame's src as you move between them. This layout's full-view half of
// layout-pages.js — see PAGE_URL below for the split-pane half.
const FRAME_PAGES = LayoutPages.CLASSIC_FULL;
const infoBox   = document.getElementById('info-box');
const ibStatus  = document.getElementById('ib-status');
// (TGP's panel/img + has-feed handling live in src/web/pages/tgp/, hosted in #page-frame.)
const sepEls      = document.querySelectorAll('#keys-left .sep');    // 0 = above key[0], i+1 = below key[i]
const sepElsRight = document.querySelectorAll('#keys-right .sep');   // same structure for the right column
// (No RWR element refs here — full-view RWR is hosted in #page-frame, src/web/pages/rwr/, which
//  owns the scope SVG. The shell keeps only rwrData + mwData + the forwarders below.)
// (No AVN element refs here — full-view AVN is hosted in #page-frame, src/web/pages/avn/, which
//  owns the silhouette/bars DOM. The shell keeps only avnData + the forwarders below.)
// (No WPN/CM overlay element refs here — full-view WPN is hosted in #page-frame, which owns
//  its own weapon rows + CM panel; see src/web/pages/wpn/.)

// ── Pages ─────────────────────────────────────────────────────────────────────────
// ── Navigation model ─────────────────────────────────────────────────────────────────
// The layout-independent { label, action } list per page lives in nav-model.js (loaded before this
// script) — see docs/layouts.md, "The seam". Everything below is the BEZEL layout renderer: how
// this particular shell places that model on physical keys. A second layout would consume the same
// NAV and bring its own placement.
const NAV = NavModel.NAV;

// ── Bezel layout renderer: full-view placement ───────────────────────────────────────
// NAV item i lands on left-column key i, overflowing onto the right bank once the left one's six
// keys are full (MAP's R+/R-, issue #38, is the first page past that mark). Uniform across every
// page today, so it's derived rather than declared — the answer to layouts.md's "is placement
// derivable from the ordered list?" is YES for full view (and no for split; see SPLIT_SLOTS).
function fullViewSlot(i) { return i < 6 ? { bank: 'left', index: i } : { bank: 'right', index: i - 6 }; }

// Screens this layout puts on the glass beyond NAV's. The mirror of the F-35's own MAIN_EXTRAS, and
// here for the same reason: NAV is shared and pinned at MAIN's six items (nav-model.test.js), one
// per left-bank key, so a layout that wants more names them itself. LYT could not go in NAV even if
// there were room — the F-35 already offers this choice from its master strip, so a NAV entry would
// put it on that layout's MAIN a second time.
//
// LAYOUT is three left-bank labels and nothing else — the way back, then the two layouts. It draws
// no panel: every other page in this shell puts its items beside a physical key, and a chooser is
// navigation, so it reads as one. `mark` is the layout you are already on.
const BEZEL_EXTRAS = {
  // HUD, CFG, MDT, RDR and AFM — the layout-owned MAIN items the six shared NAV items don't
  // cover. HUD opens the HUD OPTIONS #page-frame page; CFG the keybinds page, which now carries
  // KEY/LYT/RTS as a sibling group (NAV.keys, cfg-rates experiment issue #39 — mirrors MDT's own
  // sibling group below: action stays 'keys', same as MDT staying 'akf'); MDT (Mission Data Table)
  // folds AKF/MIS/OBJ/BDF/PAL together — landing on AKF by default (issue #34 follow-up; the other
  // four are a switch away via NAV.akf/NAV.mis/NAV.obj/NAV.bdf/NAV.pal) rather than its own MAIN
  // entry; AFM shows the aircraft name + damage silhouette (split out of AVN, which is avionics
  // only now).
  // All are frame-hosted pages that get their MAIN back from NAV like every other, so none needs an
  // entry of its own here beyond this one.
  // No bank/index/mark here (unlike lyt below): MAIN_SPLIT_ITEMS is the only consumer, in both full
  // view and split (showPage / renderSplitLabels' 'main' branch), and it places by alphabetical
  // order, not a fixed key.
  main: [
    { label: 'HUD', action: 'hud' },
    { label: 'CFG', action: 'keys' },
    { label: 'MDT', action: 'akf' },
    { label: 'RDR', action: 'rdr' },   // → RDR radar page (docs/rdr-page.md)
    { label: 'AFM', action: 'afm' },   // → AFM airframe page (name + damage silhouette)
  ],
  // No MAIN back-item under lyt here — picking CLASSIC already navigates back to MAIN (this shell),
  // so a separate way-back label would be redundant with it.
  lyt:  [
    { label: 'CLASSIC', action: 'lyt-classic', bank: 'left', index: 0, mark: true },
    { label: 'F-35',    action: 'lyt-f35',     bank: 'left', index: 1 },
  ],
};

// All eleven MAIN destinations, alphabetically — the single ordering both full view (showPage) and a
// split pane's paginated list (renderSplitLabels' 'main' branch) place from. Full view has room for
// all of them at once (six left-bank keys, five of the right bank's six); a split pane's budget is 6
// physical keys, too few — including HUD/KEY/LYT/BDF/PAL, which a split pane couldn't reach at all
// before (the right bank is the pane's own column there, not BEZEL_EXTRAS) — so MAIN becomes a
// paginated list there, the same idea as WPN's weapon list.
const MAIN_SPLIT_ITEMS = NAV.main.concat(BEZEL_EXTRAS.main)
  .slice()
  .sort(function (a, b) { return a.label.localeCompare(b.label); });

// action → NAV.map item, shared by every full-view/split MAP ordering below — each is just a list
// of actions mapped through this once, rather than NAV.map's own array order (issue #38 follow-up:
// full view now wants a fixed 5-left/5-right split that doesn't line up with NAV.map's own order,
// same reasoning MAIN's full view already diverges from NAV.main via MAIN_SPLIT_ITEMS).
const mapItemByAction = (function () {
  const byAction = {};
  NAV.map.forEach(function (item) { byAction[item.action] = item; });
  return byAction;
})();

// NAV.map's own order for split pagination — deliberately NOT NAV.map's own full-view order (same
// divergence MAIN_SPLIT_ITEMS already has from full view's own MAIN placement). The order itself
// (SplitSlots.MAP_SPLIT_ORDER/MAP_SPLIT_ORDER_V/mapSplitOrder) lives in split-slots.js, tested there
// against NAV.map directly — see those constants' own comments for why this reordering exists (the
// ROUTE decorator bug), why 'h' vs 'v'/'vw' get different orders (WPT-leads-in-v-split follow-up:
// only 'h' has a left/right bank split a pair could straddle), and why R+/R- filter out with no
// route saved at all while W+/W- filter out with no route ACTIVE (issue #38 follow-up, deactivate
// follow-up, mirroring MAP_FULL_RIGHT below). Read live rather than cached at load, same reason
// MAP_FULL_RIGHT's route check is: routes/active route/orientation can all change without a reload.
function mapSplitItems() {
  const c = WaypointsStore.load();
  return SplitSlots.mapSplitOrder(splitVariant, c.routes.length > 0, !!WaypointsStore.getActiveRoute()).map(function (a) { return mapItemByAction[a]; });
}

// MAP's full-view placement (issue #38 follow-up): a fixed 5-left/5-right split instead of the
// generic 6-then-overflow fullViewSlot sweep every other page uses — MAIN/GRID/FLW/Z+/Z- read as
// "map view controls" on the left, WPT/R+/R-/W+/W- as "waypoint controls" on the right. The action
// lists themselves (SplitSlots.MAP_FULL_LEFT/RIGHT) live in split-slots.js, shared with f35.js's own
// glass placement so the two layouts can't drift apart — see that module's own comment. showPage's
// 'map' branch conditionally drops rt-next/rt-prev from the right list (via SplitSlots.mapFullRight)
// when no route is saved at all, and wpt-next/wpt-prev when none is active — WPT itself always
// stays, since it's how a pilot gets a route in the first place.
const MAP_FULL_LEFT = SplitSlots.MAP_FULL_LEFT.map(function (a) { return mapItemByAction[a]; });
function mapFullRight(hasRoutes, hasActiveRoute) {
  return SplitSlots.mapFullRight(hasRoutes, hasActiveRoute).map(function (a) { return mapItemByAction[a]; });
}

// Which pages draw an OPAQUE full-view overlay. MAIN paints a panel over the still-running map, and
// LAYOUT is a menu with nothing of its own behind it; every other page is transparent (its content
// is the map, or the #page-frame beneath).
const OPAQUE_PAGES = { main: true, lyt: true };
let currentPage = 'map';

// ── Split-screen state ──────────────────────────────────────────────────────────────
// When splitMode is on, the screen renders two stacked iframes (the panes) instead
// of the single map iframe + overlay panels. Each pane has its own currentPage;
// the shell still owns the bezel labels and dispatches clicks to the right pane.
// See docs/mfd-split-screen.md — Strategy A, implementation sequence steps 1-4.
let splitMode = false;
// Split orientation: 'h' = top/bottom (H_SPLIT), 'v' = left/right 50/50 (V_SPLIT),
// 'vw' = left/right 2:1 (V_WIDE_SPLIT). Drives the .split-<variant> CSS class and the
// bezel key mapping (SplitKeymap.paneKey). Meaningful only while splitMode is on.
let splitVariant = 'h';
// [topPage, botPage]. Step 3 of the implementation sequence seeds both panes with
// MAIN on entry; per-pane navigation updates this from MAIN's L0..L2 / R0..R2 keys.
let panePages = ['main', 'main'];
// Per-pane WPN pagination index. WPN's weapon list can exceed one split page; each pane
// scrolls independently via its PREV/NEXT bezel labels. Reset to 0 when a pane (re)enters
// WPN. The bare WPN page is a pure renderer — the shell slices the list here.
//
// A split pane shows at most 4 weapons (slots L1, L2, R1, R2). The top band's keys are
// reserved: L0 = MAIN/PREV back-button, R0 = NEXT (shown only when the loadout exceeds 4).
let paneWpnPage = [0, 0];
const WPN_SPLIT_MAX = ClassicPaging.WPN_SPLIT_MAX;

// AVN split pagination: a pane shows 4 of the 8 avn.toggle groups per page (item slots L1,L2,R1,R2
// like WPN's), PREV/NEXT on the pane's top/bottom keys via listPaneLayout. Reset to 0 when a pane
// (re)enters AVN — the groups never reorder, so unlike WPN there's no "selection" to auto-page to.
let paneAvnPage = [0, 0];
// The slice itself is pure (classic-paging.js); these wrappers just bind it to this pane's page
// state and write the clamped index back, so every call site keeps its original signature.
function avnPaneSlice(idx) {
  const slice = ClassicPaging.avnPaneSlice(AVN_TOGGLE_GROUPS, paneAvnPage[idx]);
  paneAvnPage[idx] = slice.pageIndex;
  return slice;
}
// Wire this pane's 4 visible avn.toggle groups to the physical keys L.items resolves to — mirrors
// wireWpnPaneWeaponKeys, plus an overlay text label (avnNavLabelText, the same abbreviations full
// view's placeAvnNavLabels uses) since a split pane only exposes 4 of the 8 keys at a time and a
// pilot can't tell which is which from the icon grid alone (unlike full view, where all 8 keys
// have a fixed 1:1 row). No on/off colouring on the label itself — that state already shows on the
// page's own tile grid, same as full view's labels.
function wireAvnPaneToggleKeys(groups, L, paneTag) {
  for (let i = 0; i < L.items.length && i < groups.length; i++) {
    const key = keyBanks[L.items[i].bank][L.items[i].index];
    if (!key) continue;
    key.dataset.group = groups[i];
    placeSplitKey(L.items[i], avnNavLabelText(groups[i]), 'avn.toggle', paneTag);
  }
}

// ARM/SAFE/A-A/A-G (docs/radar-master-arms.md) in a split pane, and the pair-never-straddles-a-page
// rule that shapes the slot list, live in classic-paging.js alongside the rest of the pagination.
const buildWpnSplitPages = ClassicPaging.buildWpnSplitPages;

// Per-pane MAIN pagination index — MAIN_SPLIT_ITEMS paged across the pane's keys (mainPaneSlice).
// Reset to 0 when a pane (re)enters MAIN (paneNavigate), same as paneWpnPage for WPN.
let paneMainPage = [0, 0];

// Per-pane MAP pagination index — NAV.map paged across the pane's keys the same way MAIN's list
// is (mapNavPaneSlice), since issue #38's R+/R- and W+/W- pushed NAV.map's 10 items past a split
// pane's 6-key budget. Reset to 0 when a pane (re)enters MAP (paneNavigate).
let paneMapNavPage = [0, 0];

// Latest connection status mirrored from the map iframe — kept so we can push the
// current value to a freshly-loaded pane iframe (its onload may fire AFTER the
// shell has already received and forwarded the last status broadcast).
let lastStatusCls  = 'disconnected';
let lastStatusText = '● DISCONNECTED';

// ── Bezel layout renderer: split placement ───────────────────────────────────────────
// The pane-local { side, slot } table for each split-capable page — index-aligned with NAV[page].
// Lives in its own module (split-slots.js) so it can be unit-checked in Node against NAV's shape;
// see that file's header for the full rationale and per-page notes.
const SPLIT_SLOTS = SplitSlots.SPLIT_SLOTS;

// URL for each iframe-served page — this layout's half of layout-pages.js, which keeps it beside
// the F-35's table so the two can't quietly diverge. Pages without an entry render 'about:blank'
// on navigation (paneUrl), a no-op signal rather than a crash.
const PAGE_URL = LayoutPages.CLASSIC_SPLIT;
function paneUrl(page) { return PAGE_URL[page] || 'about:blank'; }

// Map a pane's pane-local (side, slot) label position to the physical bezel key {bank, index}
// for the current split orientation (see split-keymap.js). Used by every split-mode key placer.
function paneKey(paneIdx, side, slot) { return SplitKeymap.paneKey(splitVariant, paneIdx, side, slot); }

// Apply the split CSS classes: `.split` gates the shared split rules; `.split-<variant>` picks
// the orientation (h = top/bottom, v = left/right 50/50, vw = left/right 2:1).
function applySplitClasses() {
  screenEl.classList.toggle('split', splitMode);
  screenEl.classList.remove('split-h', 'split-v', 'split-vw');
  if (splitMode) screenEl.classList.add('split-' + splitVariant);
}

// Enter a split (seeding the top/left pane from the current full-view page, the other from MAIN),
// or — if already split — just switch orientation, keeping each pane's page + scroll state and
// only re-laying the container (CSS) and re-mapping the bezel labels to the new key columns.
function setSplit(variant) {
  // Flipping the split axis (H<->V family) moves which pane is top-right, so the pin no longer
  // points at that corner — clear it. Staying within the V family (v<->vw) keeps the same pane.
  if (splitMode && (splitVariant === 'h') !== (variant === 'h')) clearPin();
  splitVariant = variant;
  if (splitMode) {
    applySplitClasses();
    renderSplitLabels();            // key mapping is orientation-dependent
    // Re-forward list-page geometry so WPN/AVN panes re-lay-out for the new orientation (else they
    // keep the previous layout's row arrangement — e.g. H's 2-column grid in a V column).
    forwardWpnLayoutToPanes();
    forwardAvnLayoutToPanes();
    positionSoiRing();              // the panes moved (axis flip / v↔vw); re-frame the focused one
    return;
  }
  splitMode = true;
  panePages = [PAGE_URL[currentPage] ? currentPage : 'main', 'main'];
  applySplitMode();
}

function applySplitMode() {
  // Crossing the full<->split boundary changes what PIN/SWAP target (the single stack vs. the
  // top-right pane), so the two contexts never share a pin — start each side clean. Same-axis
  // and v<->vw reconfigs return early in setSplit and never reach here, so they keep their pin.
  clearPin();
  applySplitClasses();
  // The vertical-MAIN overlay style is full-view only (TGT / HUD / BDF / PAL); split entry doesn't
  // go through showPage, so drop it here or its label style would leak onto the split MAIN labels.
  // Restored on unsplit (showPage re-toggles it). TGT, HUD, BDF and PAL split into a pane like the
  // other frame pages (they get their upright pane MAIN via the per-label vlabel class instead);
  // LYT does not (not in PAGE_URL — it's a whole-document layout switch, not per-pane content), so
  // picking it from a pane collapses the split instead (mfdButton's pane branch).
  overlayEl.classList.remove('vmain');
  if (splitMode) {
    paneFollowOn = [false, false];   // fresh panes; follow restarts off, re-reported on load
    paneGridOn = [false, false];     // fresh panes; grid guessed off (its default), re-reported on load
    paneIframes[0].src = paneUrl(panePages[0]);
    paneIframes[1].src = paneUrl(panePages[1]);
    renderSplitLabels();
    refreshFollowIndicator();
  } else {
    // Drop iframe sources so they stop holding resources while hidden.
    paneIframes[0].removeAttribute('src');
    paneIframes[1].removeAttribute('src');
    // Re-render the single-pane layout for whatever page was current before.
    showPage(currentPage);
    refreshFollowIndicator();        // prune any split-mode FOLLOW chip; single-mode recompute
  }
  // The surface count just changed (1 full view ↔ 2 split); tell the server so SOI cycles the right
  // surfaces, and re-place the ring, which now frames a pane instead of the whole recess (or back).
  reportPanes();
  positionSoiRing();
}

// Place per-pane labels for both panes' current pages. Each pane-local (side, slot) resolves to
// a physical key via paneKey, which depends on the split orientation: top/bottom (H) gives each
// pane both columns (pane 1 offset +3); left/right (V/VW) gives each pane its own column. Labels
// are tagged with data-pane so the click dispatcher knows which pane to update.
function isListPage(page) { return page === 'wpn' || page === 'avn'; }

// The 8 avn.toggle groups, in the AVN page's own reading order (GEAR/RADAR/GUNS/ENG down the
// left column, ASSIST/NVG/LIGHTS/TURRET down the right — see avn.js). Shared by full view (which
// shows all 8 at once, no pagination needed — 8 fit in the 8 spare left/right keys) and split
// (which pages through them 4 at a time, mirroring WPN's split pagination).
const AVN_TOGGLE_GROUPS = ['gear', 'radar', 'guns', 'eng', 'assist', 'nvg', 'lights', 'turret'];

// Physical keys for a paginated list page (WPN, MAIN) in split pane paneIdx, per orientation:
//   h    → MAIN at the pane's L0, the 4 rows at L1,L2 + 2 of R0-R2, NEXT at the remaining R slot.
//          WPN keeps its long-standing NEXT at R0 (top-right); every other list (MAIN's own)
//          puts NEXT at R2 (bottom-right) instead, so it doesn't read as if it were the way back.
//   v/vw → one column: MAIN at key 0 (top), the 4 rows at keys 1..4, NEXT at key 5 (bottom) —
//          same for every list page, so `page` only matters to the 'h' branch.
// Returns {bank,index} for main/next/each item, plus the per-item side class the page renders with.
function listPaneLayout(paneIdx, page) {
  return ClassicPaging.listPaneLayout(splitVariant, paneIdx, page);
}

// Place an overlay label on a physical key {bank,index} and tag it with the owning pane. Returns the
// label element so the caller can style it (e.g. the vertical MAIN for a TGT pane). `mark` lights it
// engaged amber, same as placeOverlayLabel's own param — used by WPN's ARM/SAFE/A-A/A-G.
function placeSplitKey(m, label, action, paneTag, mark) {
  const el = placeOverlayLabel(m.bank, m.index, label, action, mark);
  const k = keyBanks[m.bank] && keyBanks[m.bank][m.index];
  if (k) k.dataset.pane = paneTag;
  return el;
}

// Pages whose own content sits in the top-left where the MAIN bezel label lands, so that label is
// stood upright to clear it — in full view via .overlay.vmain, in a split pane via a per-label class
// (renderSplitLabels). TGT's RESET FILTER, HUD's mode/category rows, and BDF/PAL/MIS/OBJ's WARHEADS
// readout are that content — on a narrow display the panel widens to the edge and a horizontal MAIN
// would sit over that header. All are split-capable.
// RDR dropped out of this list (issue #40 follow-up): it used to carry only MAIN, cramped enough to
// need the narrow vertical treatment, but now has MAIN + R+ + R- and reads fine horizontal.
// KEY dropped out too (cfg-rates experiment, issue #39): the CFG group's nav labels read fine
// horizontal, per user preference — its table header sits far enough from the bezel edge.
function isVmainPage(p) { return p === 'tgt' || p === 'hud' || p === 'akf' || p === 'bdf' || p === 'pal' || p === 'mis' || p === 'obj'; }

// The item count on each MAIN split page. Unlike WPN, MAIN reserves no fixed back-slot: PREV anchors
// the first key only on pages past the first, NEXT the last key only on pages before the last, and
// items fill every slot in between — INCLUDING the first when there's no PREV and the last when
// there's no NEXT. So the page sizes are chosen to fill all six keys: the first page holds five (no
// PREV), a middle page four (PREV + NEXT both eat a slot), and the last page up to five (no NEXT).
// The first page a split opens on is therefore full, not four items with two empty keys.
function mainPageSizes() {
  return ClassicPaging.mainPageSizes(MAIN_SPLIT_ITEMS.length);
}

// This pane's slice of the MAIN list, with the page clamped in range.
function mainPaneSlice(idx) {
  const slice = ClassicPaging.mainPaneSlice(MAIN_SPLIT_ITEMS, paneMainPage[idx]);
  paneMainPage[idx] = slice.pageIndex;
  return slice;
}

// This pane's slice of MAP_SPLIT_ITEMS, same shape as mainPaneSlice above (issue #38).
function mapNavPaneSlice(idx) {
  const slice = ClassicPaging.mainPaneSlice(mapSplitItems(), paneMapNavPage[idx]);
  paneMapNavPage[idx] = slice.pageIndex;
  return slice;
}

function renderSplitLabels() {
  clearKeyActions();
  // .wpn-decor too: full view's MASTER/MODE and ZOOM decorators (docs/radar-master-arms.md,
  // issue #41) must not survive entering split mode — split doesn't place them.
  overlayEl.querySelectorAll('.overlay-item, .wpn-decor').forEach(function(el) { el.remove(); });
  for (let paneIdx = 0; paneIdx < 2; paneIdx++) {
    const page = panePages[paneIdx];
    const paneTag = paneIdx === 0 ? 'top' : 'bot';   // pane identity for click dispatch (orientation-agnostic)

    if (page === 'main') {
      // MAIN_SPLIT_ITEMS instead of SPLIT_SLOTS/NAV.main — see mainPaneSlice/mainPageSizes. PREV
      // anchors the first physical key, NEXT the last, and the page's items fill every key in between
      // — and the first key too when there's no PREV, the last when there's no NEXT. The page sizes
      // keep those free keys exactly filled, so the first page shows NEXT in the last slot with no
      // gaps, a middle page has PREV first and NEXT last, and the last page has PREV first and no NEXT.
      const L = listPaneLayout(paneIdx, 'main');
      const positions = [L.main, L.items[0], L.items[1], L.items[2], L.items[3], L.next];
      const slice = mainPaneSlice(paneIdx);
      const cells = new Array(positions.length).fill(null);
      if (slice.hasPrev) cells[0] = { label: 'PREV', action: 'main-prev' };
      if (slice.hasNext) cells[cells.length - 1] = { label: 'NEXT', action: 'main-next' };
      let it = 0;
      for (let p = 0; p < cells.length; p++) {
        if (cells[p] === null && it < slice.items.length) {
          cells[p] = { label: slice.items[it].label, action: slice.items[it].action };
          it++;
        }
      }
      cells.forEach(function (cell, i) { if (cell) placeSplitKey(positions[i], cell.label, cell.action, paneTag); });
      continue;
    }

    if (page === 'map') {
      // MAP's own list paging (issue #38) — NAV.map grew past a split pane's 6-key budget once
      // R+/R- were added, so it's paginated exactly like MAIN above (mapNavPaneSlice/mainPageSizes)
      // rather than declaring SPLIT_SLOTS.map slots (map has none — see split-slots.js).
      const L = listPaneLayout(paneIdx, 'map');
      const positions = [L.main, L.items[0], L.items[1], L.items[2], L.items[3], L.next];
      const slice = mapNavPaneSlice(paneIdx);
      const cells = new Array(positions.length).fill(null);
      if (slice.hasPrev) cells[0] = { label: 'PREV', action: 'map-nav-prev' };
      if (slice.hasNext) cells[cells.length - 1] = { label: 'NEXT', action: 'map-nav-next' };
      let it = 0;
      for (let p = 0; p < cells.length; p++) {
        if (cells[p] === null && it < slice.items.length) {
          cells[p] = { label: slice.items[it].label, action: slice.items[it].action };
          it++;
        }
      }
      cells.forEach(function (cell, i) { if (cell) placeSplitKey(positions[i], cell.label, cell.action, paneTag); });
      // ZOOM/ROUTE decorators only render when both keys of their pair landed on the SAME page —
      // a rare pagination edge case (mainPageSizes has no pairing awareness), skipped rather than
      // drawn wrong, same reasoning as WPN's MASTER/MODE split-pane decorators.
      placeMapPaneDecorator(positions, cells, 'zin', 'zout', 'ZOOM');
      placeMapPaneDecorator(positions, cells, 'rt-next', 'rt-prev', 'ROUTE');
      placeMapPaneDecorator(positions, cells, 'wpt-next', 'wpt-prev', 'WYPT');
      continue;
    }

    const slots = SPLIT_SLOTS[page];
    if (!slots) continue;                            // not a split-capable page (e.g. LYT)

    if (page === 'avn') {
      // Paginated avn.toggle groups: MAIN (or PREV once scrolled) on the pane's top key, NEXT on
      // its bottom key, 4 of the 8 groups on the item slots — same shape as WPN's split, own copy
      // since AVN's slice is a plain fixed-list page (no weapons/controls mix to interleave).
      const L = listPaneLayout(paneIdx, page);
      const slice = avnPaneSlice(paneIdx);
      placeSplitKey(L.main, slice.hasPrev ? 'PREV' : 'MAIN', slice.hasPrev ? 'avn-prev' : 'main', paneTag);
      if (slice.hasNext) placeSplitKey(L.next, 'NEXT', 'avn-next', paneTag);
      wireAvnPaneToggleKeys(slice.items, L, paneTag);
    } else if (isListPage(page)) {
      // Paginated list (WPN): MAIN (or PREV once scrolled) on the pane's top key, NEXT on its
      // bottom key — positions per orientation via listPaneLayout; the 4 rows sit on .items.
      const L = listPaneLayout(paneIdx, page);
      const slice = wpnPaneSlice(paneIdx);
      placeSplitKey(L.main, slice.hasPrev ? 'PREV' : 'MAIN', slice.hasPrev ? 'wpn-prev' : 'main', paneTag);
      if (slice.hasNext) placeSplitKey(L.next, 'NEXT', 'wpn-next', paneTag);
      // Wire this pane's weapon rows to weapon.select, tagged with the owning pane so the SOI
      // cursor (soiKeys(), scoped per pane) can reach them — dispatch itself doesn't need the tag
      // (weapon selection is aircraft-global and falls through to the shared case regardless).
      wireWpnPaneWeaponKeys(slice.items, paneIdx, paneTag);
      // ARM/SAFE/A-A/A-G (docs/radar-master-arms.md) — appended after the weapon list into the same
      // 4 item slots (buildWpnSplitPages), since a split pane has no spare keys the way full view
      // does. Weapon slots are already wired above; only 'ctrl' slots need placing here ('empty'
      // slots need nothing — clearKeyActions already cleared them).
      slice.slots.forEach(function(s, i) {
        if (s.type !== 'ctrl') return;
        const mark = s.id === 'master-arms-on'  ? wpnData.masterArmsOn === true
                   : s.id === 'master-arms-off' ? wpnData.masterArmsOn === false
                   : s.id === 'combat-mode-aa'  ? wpnData.combatMode === 'aa'
                   :                              wpnData.combatMode === 'ag';
        placeSplitKey(L.items[i], s.label, s.id, paneTag, mark);
      });
      // MASTER/MODE decorators (docs/radar-master-arms.md) — never wired up for split before; full
      // view has always had them (placeWpnDecorators), split just never got the equivalent call.
      if (page === 'wpn') {
        placeWpnPaneDecorator(L, slice.slots, 'master-arms-on', 'master-arms-off', 'MASTER');
        placeWpnPaneDecorator(L, slice.slots, 'combat-mode-aa', 'combat-mode-ag', 'MODE');
      }
    } else {
      // Static nav (MAP/AVN/RWR/TGP/…): render the navigation model at this page's declared
      // pane-local slots — SPLIT_SLOTS[page][i] places NAV[page][i]. `mark` lights an item active
      // (NAV.bdf/NAV.pal's current-page flag).
      (NAV[page] || []).forEach(function(item, i) {
        const s = slots[i];
        // SPLIT_SLOTS is index-aligned with NAV, so a NAV item added without a matching slot would
        // silently not render here — the exact failure the old duplicated tables produced. Say so.
        if (!s) { console.warn('[mfd] NAV.' + page + '[' + i + '] "' + item.label + '" has no SPLIT_SLOTS entry — not placed'); return; }
        const el = placeSplitKey(paneKey(paneIdx, s.side, s.slot), item.label, item.action, paneTag, item.mark);
        // TGT/HUD keep clickable content under their MAIN label; stand it upright in the
        // pane too, the way full view does via .overlay.vmain — for those single-item pages that's
        // just MAIN. BDF/PAL/MIS/OBJ carry four MORE items each (their own MDT switch), split across
        // both the pane's left AND right columns (SPLIT_SLOTS.bdf/pal/mis/obj) — a horizontal "MIS"/
        // "OBJ" label would run wide over the pane's own content on that edge (both pages already
        // reserve only a narrow vertical-label inset on each side, matching BDF's). So every item of
        // a vmain page stands upright here, not just its MAIN back-item — full view's rule already
        // covers every left-bank item the same way (BDF/PAL/MIS/OBJ all land on the left bank there),
        // this just extends the same idea to whichever side a split pane put each item on.
        if (el && isVmainPage(page)) el.classList.add('vlabel');
      });
      // RANGE decorator between R+/R- (issue #40 follow-up) — RDR's twin. R+ is NAV.rdr[1]/
      // SPLIT_SLOTS.rdr[1]; paneKey resolves its physical key for this pane/orientation.
      if (page === 'rdr') placeRdrDecorators(paneKey(paneIdx, slots[1].side, slots[1].slot));
    }
  }
  renderPaneMainPageInd();   // main-prev/next (mfdButton) calls renderSplitLabels directly, not
                             // refreshFollowIndicator, so the chip needs its own call here too.
  renderSoiCursor();         // same reason: the labels this just rebuilt carry the cursor mark
  markFollowLabels();        // ...and the FLW label carries the follow state
  markGridLabels();          // ...and the GRID label carries the grid-overlay state
  syncCursorFocus();         // a pane may have paged onto/off MAP under the focused surface
}

// Send a map action (toggle-follow / zoom-in / zoom-out) to a single pane's map iframe.
// Same protocol the shell uses for the full-view map (mapSend), but targeted at one pane.
function paneMapSend(paneIdx, action) {
  const w = paneIframes[paneIdx].contentWindow;
  if (w) w.postMessage({ mfd: true, action: action }, '*');
}

function paneNavigate(paneIdx, page) {
  panePages[paneIdx] = page;
  if (page === 'wpn') paneWpnPage[paneIdx] = Math.max(0, selWeaponPage());   // open on the selected weapon's page
  if (page === 'main') paneMainPage[paneIdx] = 0;   // fresh pane always opens on MAIN's first page
  if (page === 'map')  paneMapNavPage[paneIdx] = 0; // fresh pane always opens on MAP's first nav page
  if (page === 'avn')  paneAvnPage[paneIdx]  = 0;   // fresh pane always opens on the first 4 groups
  paneFollowOn[paneIdx] = false;   // iframe reloads; follow restarts off (re-reported on load)
  paneIframes[paneIdx].src = paneUrl(page);
  renderSplitLabels();
  refreshFollowIndicator();        // entering/leaving MAP changes whether the chip shows
}

// Forwarding from shell → pane iframes. The shell already mirrors all the data
// streams from the map iframe (status, avn, tgp, etc.); this just relays the
// latest snapshot to whichever pane needs it.
function forwardStatusToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'main') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'status', cls: lastStatusCls, text: lastStatusText }, '*');
  });
}
function forwardAvnToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'avn') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({
      mfd: true, type: 'avn',
      name: avnData.name,
      parts: avnData.parts,
      failures: avnData.failures,
      fuel: avnData.fuel,
      throttle: avnData.throttle,
      heat: avnData.heat,
      heatColor: avnData.heatColor,
      rpm: avnData.rpm,
      hasAb: avnData.hasAb,
      abStart: avnData.abStart,
      gearDown: avnData.gearDown,
      radar: avnData.radar,
      guns: avnData.guns,
      ignition: avnData.ignition,
      assist: avnData.assist,
      turret: avnData.turret,
      nvg: avnData.nvg,
      navLights: avnData.navLights,
      // No `visible`/page/pages any more: the icon grid always shows all 8, split or full — only
      // the pane's 4 physical toggle KEYS still page (avnPaneSlice, renderSplitLabels' avn branch),
      // same as WPN's list ever needing more rows than keys. The grid and the keys page
      // independently now: the page's own tiles show current state at a glance regardless of which
      // 4 groups the bezel can actuate this page.
    }, '*');
  });
}
// Full-view AVN: forward the snapshot to the #page-frame iframe (same payload as the panes).
function forwardAvnToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'avn', name: avnData.name, parts: avnData.parts,
                  failures: avnData.failures, fuel: avnData.fuel, throttle: avnData.throttle,
                  heat: avnData.heat, heatColor: avnData.heatColor, rpm: avnData.rpm,
                  hasAb: avnData.hasAb, abStart: avnData.abStart,
                  gearDown: avnData.gearDown, radar: avnData.radar, guns: avnData.guns,
                  ignition: avnData.ignition, assist: avnData.assist, turret: avnData.turret,
                  nvg: avnData.nvg, navLights: avnData.navLights }, '*');
}
// Split-pane AVN geometry: the pane's 4 visible toggle rows (avnPaneSlice) sit on the same
// physical keys wireAvnPaneToggleKeys just wired — forward their vertical centres (+ per-item
// side) the same way forwardWpnLayoutToPanes does for weapon rows, so the page can position its
// tiles without knowing about bezel keys itself.
function forwardAvnLayoutToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'avn') return;
    if (!iframe.contentWindow) return;
    const paneTop = iframe.getBoundingClientRect().top;
    const L = listPaneLayout(idx, 'avn');
    function cyOf(m) { const r = keyBanks[m.bank][m.index].getBoundingClientRect(); return r.top + r.height / 2 - paneTop; }
    iframe.contentWindow.postMessage({
      mfd: true, type: 'avn-layout', layout: 'compact',
      slotYs: L.items.map(cyOf), sides: L.itemSides,
    }, '*');
  });
}
// Forward the full-view geometry: AVN's content block (icon grid + gauges) centres itself in the
// band below the top bezel row, from below the first separator sep[0]'s row (sep1) to the bottom
// strip. No more per-tile leftSlots/rightSlots — the page's icon grid is a plain CSS grid now,
// not anchored to individual bezel-key rects (see placeAvnNavLabels below for where that anchoring
// moved to instead).
function forwardAvnLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  const geom = {};
  if (sepEls.length >= 2) {
    const sep0 = sepEls[0].getBoundingClientRect();   // top separator (above key[0])
    const botSep = sepEls[sepEls.length - 1].getBoundingClientRect();
    // The content band spans the WHOLE key column — from above key[0] down to the bottom strip —
    // rather than starting below key[0]'s row. That row holds only the MAIN label, and the page's
    // icon grid already insets itself past the bezel-label zone on both sides (avn.css
    // .avn-icon-grid padding), so reserving a full row for one short word just left dead space at
    // the top with the gauges squeezed below it.
    geom.frameTop     = sep0.bottom - frameTop;
    geom.frameHeight  = botSep.top - sep0.bottom;
  }
  w.postMessage({ mfd: true, type: 'avn-layout', layout: 'full', geom: geom }, '*');
}

// AFM (airframe: name + damage silhouette) shares avnData's name/parts/failures — the shell
// already tracks them for AVN's own snapshot (unused there since the redesign that split this
// page out), so AFM just reads the same fields rather than parsing its own copy. No per-pane
// layout forwarding, unlike AVN: AFM has no bezel-actuated content in a split pane, so compact's
// fixed CSS offsets are enough there — only full view needs its bezel-anchored geometry, below.
function forwardAfmToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'afm') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({
      mfd: true, type: 'afm', name: avnData.name, parts: avnData.parts, failures: avnData.failures, pylons: avnData.pylons,
    }, '*');
  });
}
// Full-view AFM: forward the snapshot to the #page-frame iframe (same payload as the panes).
function forwardAfmToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({
    mfd: true, type: 'afm', name: avnData.name, parts: avnData.parts, failures: avnData.failures, pylons: avnData.pylons,
  }, '*');
}
// Forward the full-view geometry: AFM's name band fills the top bezel row — from below the first
// separator sep[0] to above the second sep[1] — and the silhouette frame spans from below sep[1]
// to the bottom strip (last sep). Mirrors AVN's own forwardAvnLayoutToFrame from before its
// redesign dropped the header band (that page no longer needs one; this one does).
function forwardAfmLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  const geom = {};
  if (sepEls.length >= 2) {
    const sep0 = sepEls[0].getBoundingClientRect();   // top separator (above key[0])
    const sep1 = sepEls[1].getBoundingClientRect();   // below key[0] — bottom of the top bezel row
    const botSep = sepEls[sepEls.length - 1].getBoundingClientRect();
    geom.headerTop    = sep0.bottom - frameTop;       // name band …
    geom.headerHeight = sep1.top - sep0.bottom;       // … the top bezel row
    geom.frameTop     = sep1.bottom - frameTop;       // silhouette starts below sep[1]
    geom.frameHeight  = botSep.top - sep1.bottom;
  }
  w.postMessage({ mfd: true, type: 'afm-layout', layout: 'full', geom: geom }, '*');
}

// Shell-drawn NAV label per avn.toggle group (full view only — docs note this is a CLASSIC-bezel
// pass; split pane keeps its existing label-less wireAvnPaneToggleKeys wiring unchanged), at the
// same 8 physical keys wireAvnToggleKeysFull used to wire blind: left[1..4] then right[1..4]
// (left[0] stays MAIN, via the generic full-view NAV sweep in showPage; left[5]/right[0]/right[5]
// are spare). Clicking still dispatches avn.toggle the same way — this only ADDS a visible label so
// a bezel key finally says what it does. Plain white text like every other NAV label (no on/off/
// gear-down colouring here — that state already shows on the page's own tile grid). Built once per
// page entry; the 8 groups never change, so unlike WPN's loadout-driven labels this never needs a
// per-tick re-place.
function placeAvnNavLabels() {
  for (let i = 0; i < 4; i++) {
    placeAvnLabel('left',  i + 1, AVN_TOGGLE_GROUPS[i]);
    placeAvnLabel('right', i + 1, AVN_TOGGLE_GROUPS[i + 4]);
  }
}
function placeAvnLabel(bankName, keyIndex, group) {
  const key = keyBanks[bankName][keyIndex];
  if (!key) return;
  key.dataset.action = 'avn.toggle';
  key.dataset.group  = group;
  placeOverlayLabel(bankName, keyIndex, avnNavLabelText(group), 'avn.toggle');
}
// Labels over 4 characters get shortened by dropping vowels (RADAR -> RDR, TURRET -> TRRT) — these
// bezel labels sit in a narrow fixed-width column (see .avn-icon-grid's matching inset in avn.css),
// so a shorter label reads cleanly at a glance instead of getting cramped. Plain vowel-stripping
// reads oddly for ASSIST/LIGHTS (SSST, LGHTS) — hand-picked instead of forcing a formula to fit two
// exceptions. 4-and-under names (GEAR, GUNS, ENG, NVG) are already short enough, unchanged.
const AVN_LABEL_ABBR = { assist: 'ASST', lights: 'LGHT' };
function avnNavLabelText(group) {
  if (AVN_LABEL_ABBR[group]) return AVN_LABEL_ABBR[group];
  const upper = group.toUpperCase();
  return upper.length > 4 ? upper.replace(/[AEIOU]/g, '') : upper;
}
function forwardTgpToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgp') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'tgp', active: tgpActive }, '*');
  });
}
// Full-view TGP: forward the lock flag to the #page-frame iframe (the page toggles its feed).
// No geometry to forward — the feed is a single centred box, not key-band rows.
function forwardTgpToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'tgp', active: tgpActive }, '*');
}
function forwardRwrToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'rwr') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'rwr', items: rwrData.items || [] }, '*');
  });
}
// Full-view RWR: forward the contact + missile streams to the #page-frame iframe (same payloads
// as the panes). RWR is one responsive SVG, so there's no geometry to forward.
function forwardRwrToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'rwr', items: rwrData.items || [] }, '*');
}
// RDR (docs/rdr-page.md): forward the whole B-scope block (present/range/cone/hdg/items) to the
// pane(s) or the full-view frame. Like RWR it's one responsive SVG — no geometry to forward.
function forwardRdrToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'rdr' || !iframe.contentWindow) return;
    iframe.contentWindow.postMessage(rdrMsg(), '*');
  });
}
function forwardRdrToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(rdrMsg(), '*');
}
function rdrMsg() {
  return { mfd: true, type: 'rdr', present: rdrData.present, range: rdrData.range,
           cone: rdrData.cone, metric: rdrData.metric, radarOn: rdrData.radarOn,
           levelTime: rdrData.levelTime, hdg: rdrData.hdg, items: rdrData.items || [],
           pb: rdrData.pb || [] };
}
function forwardMwToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'mw', items: mwData.items || [] }, '*');
}
// Full-view TGT: forward the whole filter-state block to the #page-frame iframe. It's a plain
// state mirror (no geometry — the page is fully clickable, not bezel-anchored).
function forwardTgtToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'tgt' }, tgtData), '*');
}
// The TGT page shows the selected-target list under its filters (mirrored in targetsData).
// No pagination — the page scrolls — so forward the whole list.
function forwardTgtTargetsToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'tgt-targets', items: targetsData.targets || [] }, '*');
}
// Full-view BDF: forward the whole faction-forces block to the #page-frame iframe (docs/bdf-page.md).
// A plain state mirror, same shape as TGT — no geometry, the page isn't bezel-anchored.
function forwardBdfToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'bdf' }, bdfData), '*');
}
// Full-view PAL: same as forwardBdfToFrame, for the PRIMEVA block (docs/bdf-page.md).
function forwardPalToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'pal' }, palData), '*');
}
// Full-view MIS: forward the mission-info block (docs/mdt-pages.md). Same shape as BDF/PAL.
function forwardMisToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'mis' }, misData), '*');
}
// Full-view OBJ: forward the active-objectives list (docs/mdt-pages.md).
function forwardObjToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'obj' }, objData), '*');
}
// Full-view AKF: forward the kill-feed/session-stats block (docs/akf-page.md).
function forwardAkfToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'akf' }, akfData), '*');
}
// Split-pane twins of the two TGT forwarders — same payloads, sent to any pane showing TGT. The
// page is fully clickable inside the pane, so nothing else (no bezel-key wiring) is needed.
function forwardTgtToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgt') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'tgt' }, tgtData), '*');
  });
}
function forwardTgtTargetsToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgt') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'tgt-targets', items: targetsData.targets || [] }, '*');
  });
}
function forwardMwToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'rwr') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'mw', items: mwData.items || [] }, '*');
  });
}
// Split-pane twin of forwardBdfToFrame — same faction-forces payload, sent to any pane showing BDF.
// Read-only, so like TGT nothing else (no bezel-key wiring) is needed beyond the pane's MAIN.
function forwardBdfToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'bdf') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'bdf' }, bdfData), '*');
  });
}
// Split-pane twin of forwardPalToFrame — same as forwardBdfToPanes, for PAL panes.
function forwardPalToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'pal') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'pal' }, palData), '*');
  });
}
// Split-pane twin of forwardMisToFrame — same mission-info payload, sent to any pane showing MIS.
function forwardMisToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'mis') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'mis' }, misData), '*');
  });
}
// Split-pane twin of forwardObjToFrame — same objectives payload, sent to any pane showing OBJ.
function forwardObjToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'obj') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'obj' }, objData), '*');
  });
}
// Split-pane twin of forwardAkfToFrame — same kill-feed/session-stats payload, sent to any pane
// showing AKF.
function forwardAkfToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'akf') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'akf' }, akfData), '*');
  });
}
// Full-view WPT (issue #38): forward the mapinfo slice (position/heading/grid meta) the readout
// needs for its distance/bearing-to-next-waypoint calc.
function forwardWptToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'mapinfo' }, mapInfoData), '*');
}
// Split-pane twin of forwardWptToFrame — same payload, sent to any pane showing WPT.
function forwardWptToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'wpt') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage(Object.assign({ mfd: true, type: 'mapinfo' }, mapInfoData), '*');
  });
}
// Slice the full loadout+controls to the page a given pane is scrolled to. Returns the visible
// weapon rows (items — always a prefix of the page's 4 slots, since weapons never follow a control
// within one page, see buildWpnSplitPages) plus the raw per-slot descriptors (slots — renderSplitLabels
// uses these to place ARM/SAFE/A-A/A-G on whichever physical keys they land on) and whether
// PREV/NEXT exist. Clamps a stale page index (e.g. the loadout shrank) back into range as a side effect.
function wpnPaneSlice(idx) {
  const slice = ClassicPaging.wpnPaneSlice(wpnData.items, paneWpnPage[idx]);
  paneWpnPage[idx] = slice.pageIndex;
  return slice;
}
function forwardWpnToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'wpn') return;
    if (!iframe.contentWindow) return;
    const sl = wpnPaneSlice(idx);
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'wpn', items: sl.items, selWeapon: wpnData.selWeapon,
        softGun: wpnData.softGun, softRel: wpnData.softRel, masterArmsOn: wpnData.masterArmsOn,
        // A controls-only page legitimately sends items:[] even with a real loadout — tell the page
        // explicitly so it doesn't mistake "no weapons on THIS page" for "no loadout at all" (which
        // would wrongly show the NO LOADOUT placeholder and hide the CM panel).
        hasLoadout: (wpnData.items || []).length > 0,
        page: sl.page, pages: sl.pages }, '*');
  });
}
// 0-indexed page that holds the currently selected weapon, or -1 if there's no selection
// (or it isn't in the loadout).
function selWeaponPage() {
  return ClassicPaging.pageOfSelection(wpnData.items, wpnData.selWeapon, WPN_SPLIT_MAX);
}
// Jump every visible WPN pane to the page containing the selected weapon. Called only when the
// selection actually changes (not on every ammo/loadout tick), so a pane the user has manually
// paged elsewhere is left alone until the in-game weapon selection moves off its page.
function autoPageToSelection() {
  const page = selWeaponPage();
  if (page < 0) return;
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] === 'wpn') paneWpnPage[idx] = page;
  });
}
function forwardCmToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'wpn') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({
      mfd: true, type: 'cm',
      flares: cmData.flares, flaresMax: cmData.flaresMax,
      ewKJ: cmData.ewKJ, ewKJMax: cmData.ewKJMax, cmCat: cmData.cmCat,
    }, '*');
  });
}
// Tell each WPN pane where its weapon-row slots should sit so the rows line up with the
// physical bezel keys flanking that pane. Slot order matches the pane's fill order:
// L1, L2 (the two left keys below MAIN at L0), then R0, R1, R2. Positions are the keys'
// vertical centres in the pane iframe's own coordinate space, recomputed on load + resize.
function forwardWpnLayoutToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'wpn') return;
    if (!iframe.contentWindow) return;
    const paneTop = iframe.getBoundingClientRect().top;
    const L = listPaneLayout(idx, 'wpn');
    function rectOf(m) { return keyBanks[m.bank][m.index].getBoundingClientRect(); }
    function cyOf(m) { const r = rectOf(m); return r.top + r.height / 2 - paneTop; }
    // Weapon-row vertical centres + their per-item side class (both from the orientation layout):
    // H = L1,L2,R1,R2 across the pane; V/VW = keys 1..4 down the pane's own column.
    const slotYs = L.items.map(cyOf);
    // CM band = MAIN's key slot, between its flanking separators — measured on the pane's OWN column
    // (each column has the same sep structure) so the CM panel hugs the top the same in every layout.
    const seps = L.main.bank === 'right' ? sepElsRight : sepEls;
    const bandTop = seps[L.main.index].getBoundingClientRect().bottom - paneTop;
    const bandHeight = (seps[L.main.index + 1].getBoundingClientRect().top - paneTop) - bandTop;
    const msg = { mfd: true, type: 'wpn-layout', slotYs: slotYs, sides: L.itemSides, cmTop: bandTop, cmHeight: bandHeight };
    // Left/right split has the horizontal room top/bottom lacks, so show the selected-weapon image
    // in the pane half OPPOSITE the list (like full view). Forward the list side + the image's
    // vertical span (the weapon-row band). H_SPLIT sends no image geometry, so it stays suppressed.
    if (splitVariant !== 'h') {
      const first = rectOf(L.items[0]), last = rectOf(L.items[L.items.length - 1]);
      msg.listSide = L.itemSides[0];
      msg.iconTop = first.top - paneTop;
      msg.iconHeight = last.bottom - first.top;
    }
    iframe.contentWindow.postMessage(msg, '*');
  });
}

// Wire a split WPN pane's up-to-4 weapon rows to weapon.select. Fill order matches the pane
// renderer (wpn.js compact) and forwardWpnLayoutToPanes' slotYs: items → L1, L2, R1, R2. Sets the
// aligned physical key's action + weapon name so a bezel press selects that weapon, and tags it
// with the owning pane so the SOI cursor (soiKeys()) can reach it — dispatch itself doesn't need
// the tag (weapon selection is aircraft-global and falls through to the shared case regardless).
// Called from renderSplitLabels after clearKeyActions has cleared the key zone, so only occupied
// rows are set and empty ones stay clean.
function wireWpnPaneWeaponKeys(weapons, paneIdx, paneTag) {
  const items = listPaneLayout(paneIdx, 'wpn').items;
  for (let i = 0; i < items.length && i < weapons.length; i++) {
    const key = keyBanks[items[i].bank][items[i].index];
    if (key) { key.dataset.action = 'weapon.select'; key.dataset.wname = weapons[i].n; key.dataset.pane = paneTag; }
  }
}

// ── Full-view WPN frame (single-pane) ──────────────────────────────────────────────────
// Full-view WPN is hosted in #page-frame (the src/web/pages/wpn page in its 'full' profile).
// These mirror the split forwarders but compute the full-screen geometry (5 left-column slots
// + the right-half image area + the CM band) from the bezel separators, and slice the loadout
// to the full-view page (WPN_MAX_DISPLAY, wpnPage).
function frameWin() { return pageFrame && pageFrame.contentWindow; }
// Point #page-frame at a frame-hosted page, switching its src when moving between frame pages
// (WPN ↔ TGT) and lazy-loading on first entry. No-op if it already shows that page.
function showFramePage(name) {
  const url = FRAME_PAGES[name];
  if (url && pageFrame.getAttribute('src') !== url) pageFrame.src = url;
}

function forwardWpnToFrame() {
  const w = frameWin(); if (!w) return;
  const list = wpnData.items || [];
  const total = list.length;
  const maxPage = Math.max(0, Math.ceil(total / WPN_MAX_DISPLAY) - 1);
  if (wpnPage > maxPage) wpnPage = maxPage;
  if (wpnPage < 0) wpnPage = 0;
  const start = wpnPage * WPN_MAX_DISPLAY;
  const items = list.slice(start, start + WPN_MAX_DISPLAY);
  w.postMessage({ mfd: true, type: 'wpn', items: items, selWeapon: wpnData.selWeapon,
                  softGun: wpnData.softGun, softRel: wpnData.softRel, masterArmsOn: wpnData.masterArmsOn,
                  page: maxPage > 0 ? wpnPage + 1 : 1, pages: maxPage + 1 }, '*');

  // Wire each visible weapon's LEFT line-select key (keys 1..5) to select that weapon: a bezel
  // press sends weapon.select with the row's name. The labels live inside the frame — here we
  // only attach the action to the aligned physical key. Clear the unused row keys so a shorter
  // loadout leaves no stale action. Full view only (forwardWpnToFrame runs solely on the WPN
  // page); split-mode weapon rows aren't wired yet.
  for (let k = 0; k < WPN_MAX_DISPLAY; k++) {
    const key = keyBanks.left[k + 1];
    if (!key) continue;
    if (k < items.length) { key.dataset.action = 'weapon.select'; key.dataset.wname = items[k].n; }
    else                  { delete key.dataset.action; delete key.dataset.wname; }
  }
}
function forwardCmToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'cm', flares: cmData.flares, flaresMax: cmData.flaresMax,
                  ewKJ: cmData.ewKJ, ewKJMax: cmData.ewKJMax, cmCat: cmData.cmCat }, '*');
}
// Full-view geometry, mapped into the frame's own coordinate space (sepEls are shell-side, so
// subtract the frame's top). sepEls: index 0 = above key0, i+1 = below key i (7 separators for
// 6 keys). Weapon slot k (0..4) = key k+1, spanning sep[k+1].bottom → sep[k+2].top; CM band =
// key-0 slot (sep[0].bottom → sep[1].top); the image area spans keys 1..5 (sep[1] → sep[6])
// with a 20px inset top+bottom — matching the former overlay renderWpn/renderCm.
function forwardWpnLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  function bot(i) { return sepEls[i].getBoundingClientRect().bottom - frameTop; }
  function top(i) { return sepEls[i].getBoundingClientRect().top - frameTop; }
  const slots = [];
  for (let k = 0; k < WPN_MAX_DISPLAY; k++) {
    const t = bot(k + 1), b = top(k + 2);
    slots.push({ top: t, height: Math.max(0, b - t) });
  }
  const cmTop = bot(0), cmBot = top(1);
  const icoTop = bot(1) + 20, icoBot = top(sepEls.length - 1) - 20;
  w.postMessage({ mfd: true, type: 'wpn-layout', layout: 'full', slots: slots,
                  cmTop: cmTop, cmHeight: cmBot - cmTop,
                  iconTop: icoTop, iconHeight: icoBot - icoTop }, '*');
}
// Full-view WPN nav labels (shell-owned, since pagination is shell state): left key-0 is MAIN
// on page 0 / PREV after; right key-0 is NEXT when the loadout overflows the page. ARM/SAFE and
// A/A/A-G (docs/radar-master-arms.md) are unconditional, unlike NEXT — always shown, on
// right[1..4] (weapon rows occupy left[1..5]; right[1..4] are otherwise unused in full-view WPN).
// Since this whole function reruns every loadout tick (not just on a page change), passing `mark`
// from the current wpnData.masterArmsOn/combatMode here IS the live update — no separate
// re-apply-in-place step is needed the way FLW's markFollowLabels needs one (FLW's labels persist
// across ticks; WPN's don't).
function placeWpnNavLabels() {
  overlayEl.querySelectorAll('.overlay-item, .wpn-decor').forEach(function(el) { el.remove(); });
  delete keyBanks.left[0].dataset.action;
  delete keyBanks.right[0].dataset.action;
  const total = (wpnData.items || []).length;
  const maxPage = Math.max(0, Math.ceil(total / WPN_MAX_DISPLAY) - 1);
  const cur = Math.min(Math.max(wpnPage, 0), maxPage);
  placeOverlayLabel('left', 0, cur > 0 ? 'PREV' : 'MAIN', cur > 0 ? 'wpn-prev' : 'main');
  if (cur < maxPage) placeOverlayLabel('right', 0, 'NEXT', 'wpn-next');
  placeOverlayLabel('right', 1, 'ARM',  'master-arms-on',  wpnData.masterArmsOn === true);
  placeOverlayLabel('right', 2, 'SAFE', 'master-arms-off', wpnData.masterArmsOn === false);
  // No ALL label — holding A/A or A/G already resets to ALL (see PollTapHold in Keybinds.cs), so
  // ALL just reads as neither of these two lit, the same way it does for the keybinds themselves.
  placeOverlayLabel('right', 3, 'A/A', 'combat-mode-aa', wpnData.combatMode === 'aa');
  placeOverlayLabel('right', 4, 'A/G', 'combat-mode-ag', wpnData.combatMode === 'ag');
  placeWpnDecorators();
  // This runs on every loadout tick, not only on a page change, so the SOI cursor's mark has to be
  // re-applied here too — it lives on a label this function just threw away.
  renderSoiCursor();
}

// Purely decorative — a word + triangle above/below, centered in the gap BETWEEN a control pair
// rather than on either key (docs/radar-master-arms.md, per the user's mockup-approved design;
// issue #41 reuses it for MAP's Z+/Z- as ZOOM). Vertically centered on the separator between the
// pair's two keys (sepElsRight[2] sits between right[1]/ARM and right[2]/SAFE; sepElsRight[4]
// between right[3]/A-A and right[4]/A-G; sepEls[4] between left[3]/Z+ and left[4]/Z- — each side's
// array has index i+1 = below key i). Horizontally centered on the pair's own labels rather than
// sharing their right:16px anchor — ARM/SAFE/A-A/A-G/Z+/Z- are unpadded nowrap text right-aligned
// to that edge, so two different-width words (e.g. "ARM" vs "SAFE") don't share a center; anchoring
// the decorator to that same edge made it hug the narrower word's edge instead of sitting in the
// middle of the pair.
function placeWpnDecorator(bank, sepIndex, word, upPoints, downPoints) {
  const sep = (bank === 'right' ? sepElsRight : sepEls)[sepIndex];
  const labelA = overlayEl.querySelector('[data-key="' + bank + (sepIndex - 1) + '"]');
  const labelB = overlayEl.querySelector('[data-key="' + bank + sepIndex + '"]');
  if (!sep || !labelA || !labelB) return;
  const oRect = overlayEl.getBoundingClientRect();
  const sRect = sep.getBoundingClientRect();
  const aRect = labelA.getBoundingClientRect();
  const bRect = labelB.getBoundingClientRect();
  const centerX = ((aRect.left + aRect.right) / 2 + (bRect.left + bRect.right) / 2) / 2;

  const el = document.createElement('div');
  el.className = 'wpn-decor';
  el.style.top = (sRect.top + sRect.height / 2 - oRect.top) + 'px';
  el.innerHTML =
    '<svg width="12" height="8" viewBox="0 0 12 8"><polygon points="' + upPoints + '" fill="currentColor"/></svg>' +
    '<div class="wpn-decor-word">' + word + '</div>' +
    '<svg width="12" height="8" viewBox="0 0 12 8"><polygon points="' + downPoints + '" fill="currentColor"/></svg>';
  overlayEl.appendChild(el);
  el.style.right = 'auto';
  el.style.left = (centerX - oRect.left - el.offsetWidth / 2) + 'px';
}
function placeWpnDecorators() {
  placeWpnDecorator('right', 2, 'MASTER', '6,0 12,8 0,8', '0,0 12,0 6,8');
  placeWpnDecorator('right', 4, 'MODE',   '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// Split-pane MASTER/MODE: unlike full view's fixed right2/right4, a split pane's ctrl pair can land
// on any of its 4 item slots depending on pagination (buildWpnSplitPages) — found here by id rather
// than a hardcoded position. buildWpnSplitPages pads so a pair never straddles a PAGE boundary, but
// WPN_SPLIT_MAX=4 doesn't guarantee it stays on one BANK — a pair can still straddle L.items[1]/[2]
// (the left/right column boundary), where no sensible "between" position exists; skipped rather than
// drawn somewhere wrong (a rare pagination edge case, not the common case).
function placeWpnPaneDecorator(L, slots, idA, idB, word) {
  const i0 = slots.findIndex(function(s) { return s.id === idA; });
  const i1 = slots.findIndex(function(s) { return s.id === idB; });
  if (i0 < 0 || i1 < 0) return;
  const a = L.items[i0], b = L.items[i1];
  if (!a || !b || a.bank !== b.bank || Math.abs(a.index - b.index) !== 1) return;
  placeWpnDecorator(a.bank, Math.max(a.index, b.index), word, '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// MAP's own pane-pagination twin (issue #38): same "found by action, skip if the pair straddles a
// page or a bank" reasoning as placeWpnPaneDecorator above, adapted to mapNavPaneSlice's flat
// { positions, cells } shape rather than buildWpnSplitPages' slot list. A pair CAN straddle a page
// boundary here (mainPageSizes is a plain even-fill, no pairing awareness) — when it does, the
// decorator just doesn't render on either page, same as a WPN pair straddling a bank does.
function placeMapPaneDecorator(positions, cells, actionA, actionB, word) {
  const i0 = cells.findIndex(function(c) { return c && c.action === actionA; });
  const i1 = cells.findIndex(function(c) { return c && c.action === actionB; });
  if (i0 < 0 || i1 < 0) return;
  const a = positions[i0], b = positions[i1];
  if (!a || !b || a.bank !== b.bank || Math.abs(a.index - b.index) !== 1) return;
  placeWpnDecorator(a.bank, Math.max(a.index, b.index), word, '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// MAP's twin (issue #41) — ZOOM between Z+/Z-, same word+triangle treatment. Takes Z+'s own
// physical key ({bank,index}) rather than hardcoding one: full view and each split pane/orientation
// put Z+ on a different physical key (split-keymap.js's paneKey), but SPLIT_SLOTS.map always keeps
// Z+/Z- adjacent (slot 0/1) on the same bank, so the separator directly below Z+ (index+1) is
// always the one between them, in every context.
function placeMapDecorators(zinKey) {
  placeWpnDecorator(zinKey.bank, zinKey.index + 1, 'ZOOM', '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// MAP's ROUTE decorator (issue #38) — R+/R- switch the active waypoint route, same word+triangle
// treatment as ZOOM. Takes R+'s own physical key; in full view SPLIT_SLOTS doesn't apply (MAP is
// paginated in split, see mapNavPaneSlice/placeMapPaneDecorator instead), so this is full-view only.
function placeMapRouteDecorator(rPlusKey) {
  placeWpnDecorator(rPlusKey.bank, rPlusKey.index + 1, 'ROUTE', '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// MAP's WYPT decorator (issue #38) — W+/W- manually step the active waypoint, same word+triangle
// treatment as ROUTE. Takes W+'s own physical key; full-view only (SPLIT_SLOTS doesn't apply to MAP —
// see placeMapPaneDecorator for the split-pane twin).
function placeMapWptDecorator(wPlusKey) {
  placeWpnDecorator(wPlusKey.bank, wPlusKey.index + 1, 'WYPT', '6,0 12,8 0,8', '0,0 12,0 6,8');
}
// RDR's twin (issue #40 follow-up) — RANGE between R+/R-, same word+triangle treatment. Takes R+'s
// own physical key, same reasoning as placeMapDecorators: SPLIT_SLOTS.rdr keeps R+/R- adjacent
// (slot 1/2) on the same bank in every context, so index+1 is always the separator between them.
function placeRdrDecorators(rPlusKey) {
  placeWpnDecorator(rPlusKey.bank, rPlusKey.index + 1, 'RANGE', '6,0 12,8 0,8', '0,0 12,0 6,8');
}

// ── App-wide orientation ─────────────────────────────────────────────────────────────
// A media query INSIDE an iframe evaluates against that iframe's own box, so a split
// pane (wide + short) would wrongly read landscape even when the device is portrait.
// To keep portrait/landscape rules tied to the WHOLE APP regardless of split state, the
// shell is the single source of truth: it reads the window orientation, tags its own
// <body class="portrait|landscape">, and forwards the value to each pane iframe so they
// tag their own <body> identically. Bare pages key their orientation CSS off that class
// instead of @media (orientation).
const orientMq = window.matchMedia('(orientation: portrait)');
function appOrientation() { return orientMq.matches ? 'portrait' : 'landscape'; }
function applyShellOrientation() {
  document.body.classList.toggle('portrait',  orientMq.matches);
  document.body.classList.toggle('landscape', !orientMq.matches);
}
function forwardOrientationToPane(iframe) {
  if (iframe && iframe.contentWindow)
    iframe.contentWindow.postMessage({ mfd: true, type: 'orient', orientation: appOrientation() }, '*');
}
function broadcastOrientation() { paneIframes.forEach(forwardOrientationToPane); forwardOrientationToPane(pageFrame); }
orientMq.addEventListener('change', function() { applyShellOrientation(); broadcastOrientation(); });
applyShellOrientation();

// On pane iframe load, push the latest snapshot for whichever page that pane is
// rendering — the page may have been mid-update at the moment its iframe started
// loading — plus the current app orientation (every bare page can use it).
paneIframes.forEach(function(iframe, idx) {
  iframe.addEventListener('load', function() {
    if (!splitMode) return;
    forwardOrientationToPane(iframe);
    const page = panePages[idx];
    if      (page === 'main') forwardStatusToPanes();
    else if (page === 'avn')  { forwardAvnToPanes(); forwardAvnLayoutToPanes(); }
    else if (page === 'afm')  forwardAfmToPanes();
    else if (page === 'tgp')  forwardTgpToPanes();
    else if (page === 'rwr')  { forwardRwrToPanes(); forwardMwToPanes(); }
    else if (page === 'rdr')  forwardRdrToPanes();
    else if (page === 'tgt')  { forwardTgtToPanes(); forwardTgtTargetsToPanes(); }
    else if (page === 'bdf')  forwardBdfToPanes();
    else if (page === 'pal')  forwardPalToPanes();
    else if (page === 'mis')  forwardMisToPanes();
    else if (page === 'obj')  forwardObjToPanes();
    else if (page === 'wpt')  forwardWptToPanes();
    else if (page === 'wpn')  { forwardWpnToPanes(); forwardCmToPanes(); forwardWpnLayoutToPanes(); }
    // docs/page-cursor.md, docs/map-cursor.md: a fresh document means a fresh message listener, so
    // any earlier cursor-focus post (sent the moment this pane's src changed, before its script had
    // attached one) was silently dropped — a straight re-run of syncCursorFocus() wouldn't resend it
    // either, since contentWindow's identity survives the reload and the target-unchanged check
    // would no-op. Resend directly, bypassing that dedup, whenever this freshly-loaded pane is the
    // one eligible.
    if (PAD_CURSOR_PAGES[page] && focusedCursorWindow() === iframe.contentWindow)
      iframe.contentWindow.postMessage({ mfd: true, action: 'cursor-focus', on: true }, '*');
  });
});

// Full-view frame load: push the current snapshot once it's ready (it may have started loading
// mid-update, or its src just switched between frame pages), plus orientation + layout geometry.
pageFrame.addEventListener('load', function() {
  if (splitMode || !FRAME_PAGES[currentPage]) return;
  forwardOrientationToPane(pageFrame);
  if (currentPage === 'wpn')      { forwardWpnLayoutToFrame(); forwardWpnToFrame(); forwardCmToFrame(); }
  else if (currentPage === 'tgp') { forwardTgpToFrame(); }
  else if (currentPage === 'avn') { forwardAvnLayoutToFrame(); forwardAvnToFrame(); }
  else if (currentPage === 'afm') { forwardAfmLayoutToFrame(); forwardAfmToFrame(); }
  else if (currentPage === 'rwr') { forwardRwrToFrame(); forwardMwToFrame(); }
  else if (currentPage === 'rdr') { forwardRdrToFrame(); }
  else if (currentPage === 'tgt') { forwardTgtToFrame(); forwardTgtTargetsToFrame(); }
  else if (currentPage === 'bdf') { forwardBdfToFrame(); }
  else if (currentPage === 'pal') { forwardPalToFrame(); }
  else if (currentPage === 'mis') { forwardMisToFrame(); }
  else if (currentPage === 'obj') { forwardObjToFrame(); }
  else if (currentPage === 'akf') { forwardAkfToFrame(); }
  else if (currentPage === 'wpt') { forwardWptToFrame(); }
  // docs/page-cursor.md: full-view TGT/HUD render in the shared #page-frame, which reloads (fresh
  // document, fresh listener) on every navigation onto the page — same dropped-cursor-focus gap
  // the split-pane fix above closes, just for the full-view frame instead of a pane.
  if (PAD_CURSOR_PAGES[currentPage] && focusedCursorWindow() === pageFrame.contentWindow)
    pageFrame.contentWindow.postMessage({ mfd: true, action: 'cursor-focus', on: true }, '*');
});

// Top-right indicator stack (PINNED). pinnedPage tracks which page (if any) is currently pinned.
// indicatorOrder records the chronological order indicators were turned on — the first activated
// stays at the right edge and later arrivals stack to its left, matching how chips render with
// flex-direction:row-reverse on #mfd-indicators. (FOLLOW was once a chip here too; it now lights
// its own FLW label instead — markFollowLabels.)
const indicatorsEl = document.getElementById('mfd-indicators');
let pinnedPage    = null;
// Map follow state, mirrored from the map iframe(s) via postMessage: one for the full-view map,
// and one per pane in a split, since each MAP pane follows independently. Both drive the FLW label.
let followOn      = false;
let paneFollowOn  = [false, false];
// Grid overlay state, mirrored the same way (issue #41) — one for the full-view map, one per pane.
// Default false (fresh-pane guess before the pane reports its own persisted state on load) since
// the grid defaults off.
let gridOn        = false;
let paneGridOn    = [false, false];
let indicatorOrder = [];   // ['pinned'] — kept a list, since the stack is built to hold more
// Last non-pinned page we left to jump to pinnedPage via SWAP. Lets the second SWAP
// press return there. Cleared whenever the pin itself changes (re-pin or unpin) since
// the partner relationship is tied to the current pin.
let swapPartner   = null;

// Which pane sits in the screen's top-right corner — where PIN/SWAP act in split mode. H_SPLIT
// stacks top/bottom so it's the top pane (0); the V splits sit left/right so it's the right pane (1).
function topRightPane() { return splitVariant === 'h' ? 0 : 1; }

// Drop the pin (and its SWAP partner) and pull the PINNED chip. Shared by the pin toggle-off and
// the split-axis-flip reset.
function clearPin() {
  pinnedPage = null;
  swapPartner = null;
  indicatorOrder = indicatorOrder.filter(function(x) { return x !== 'pinned'; });
  renderIndicators();
}

function indicatorVisible(name) {
  // PINNED tracks the pinned page in whichever context owns PIN: the top-right pane in split
  // mode, the single stack in full view. (FOLLOW used to live here too; it is now shown on the
  // FLW label itself — see markFollowLabels.)
  if (name === 'pinned') {
    return pinnedPage !== null &&
      (splitMode ? panePages[topRightPane()] === pinnedPage : currentPage === pinnedPage);
  }
  return false;
}

// FOLLOW is shown by lighting the FLW label in the engaged amber, rather than by a separate chip in
// the corner: the state belongs to the control that sets it, and the F-35 already reads that way
// (.nav-item.on there, the same amber outline .overlay-item.on gives here). It also scales to a
// split for free — each pane's own FLW carries its own pane's state, where the corner chips needed
// a per-pane box and a set of anchoring rules to say the same thing.
//
// Toggles the class in place instead of re-rendering: the labels are rebuilt often (page changes,
// loadout ticks), and this is called from those same paths.
function markFollowLabels() {
  ['left', 'right'].forEach(function(side) {
    (keyBanks[side] || []).forEach(function(k) {
      if (k.dataset.action !== 'flw') return;
      // In a split each pane's FLW reports its own pane; in full view there is one map.
      const on = splitMode ? !!paneFollowOn[k.dataset.pane === 'bot' ? 1 : 0] : followOn;
      const el = overlayEl.querySelector('.overlay-item[data-key="' + k.dataset.pos + '"]');
      if (el) el.classList.toggle('on', on);
    });
  });
}
// GRID's twin of markFollowLabels — same "light the label, not a corner chip" reasoning (issue #41).
function markGridLabels() {
  ['left', 'right'].forEach(function(side) {
    (keyBanks[side] || []).forEach(function(k) {
      if (k.dataset.action !== 'grid') return;
      const on = splitMode ? !!paneGridOn[k.dataset.pane === 'bot' ? 1 : 0] : gridOn;
      const el = overlayEl.querySelector('.overlay-item[data-key="' + k.dataset.pos + '"]');
      if (el) el.classList.toggle('on', on);
    });
  });
}
// Paint a "PAGE x/y" chip in the bottom-right of each pane showing MAIN with more than one page
// (mainPageSizes) — the split twin of WPN's #page-ind, but drawn on the shared overlay rather than
// inside the /main iframe: MAIN's pagination is bezel/shell state, not anything the page itself
// knows, so there's nothing to forward in. Split mode only.
function renderPaneMainPageInd() {
  const pages = mainPageSizes().length;
  [0, 1].forEach(function(i) {
    const box = document.getElementById(i === 0 ? 'mainpage-top' : 'mainpage-bot');
    const on = splitMode && panePages[i] === 'main' && pages > 1;
    box.innerHTML = on ? '<div class="mfd-chip">PAGE ' + (paneMainPage[i] + 1) + '/' + pages + '</div>' : '';
    box.classList.toggle('show', on);
  });
}
// Re-apply follow state to the FLW label(s). Called whenever follow state, pane pages, or split
// mode change — the name is kept because those call sites all mean "follow may have changed".
function refreshFollowIndicator() {
  markFollowLabels();
  markGridLabels();
  renderIndicators();
  renderPaneMainPageInd();
}

function renderIndicators() {
  indicatorsEl.innerHTML = '';
  indicatorOrder.forEach(function(name) {
    if (!indicatorVisible(name)) return;
    const el = document.createElement('div');
    el.className = 'mfd-indicator';
    el.textContent = 'PINNED';
    indicatorsEl.appendChild(el);
  });
}

// Latest loadout snapshot mirrored from the map iframe (postMessage). Even when WPN isn't
// in view we keep it fresh, so opening the page renders immediately without a round-trip.
let wpnData      = { items: [], selWeapon: null, softGun: null, softRel: null, masterArmsOn: true, combatMode: 'all' };
let wpnPage = 0;             // 0-indexed page for the weapon list pagination (full-view nav state)
const WPN_MAX_DISPLAY = ClassicPaging.WPN_MAX_DISPLAY;   // weapons per page = 5 line-select slots (keys 1..5)

// Full-view twin of selWeaponPage() — same lookup, the layout's own page size.
function selWpnPageFull() {
  return ClassicPaging.pageOfSelection(wpnData.items, wpnData.selWeapon, WPN_MAX_DISPLAY);
}

// Latest countermeasures snapshot mirrored from the map iframe.
let cmData = { flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 };

// Latest TGP feed state mirrored from the map iframe. False until the first frame is
// produced, and back to false during the 3-second post-loss hold's expiry.
let tgpActive = false;

// Latest selected-target list mirrored from the map iframe. The TGT page renders it under its
// filters (forwardTgtTargetsToFrame) — the whole list, unpaginated, since that page scrolls.
let targetsData = { targets: [] };

// Latest avionics snapshot mirrored from the map iframe. name = aircraft display name (also
// the key for /airframe + /airframe-layout); parts = the live HP list from the snapshot;
// failures = list of failure-message strings currently active (e.g. ["LEFT ENGINE FIRE"]).
// Latest AVN snapshot, mirrored from the map iframe's SSE feed. The shell keeps only this
// state (the forwarders read it); all rendering — silhouette, failure labels, FUEL/THROTTLE
// bars, the failure-label parsing/placement, the /airframe layout cache — lives in src/web/pages/avn/.
let avnData = { name: null, parts: null, failures: null, pylons: null, fuel: -1, throttle: -1, heat: -1, heatColor: null, rpm: -1, hasAb: false, abStart: 1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false };

// Latest RWR emitters + incoming missiles, mirrored from the map iframe's SSE feed. The shell
// keeps only this state (the forwarders read it); all scope SVG rendering lives in src/web/pages/rwr/.
let rwrData = { items: [] };
let mwData  = { items: [] };

// Latest RDR B-scope block (docs/rdr-page.md), mirrored from the map iframe's SSE feed. present is
// false when the aircraft has no radar; the page draws its own scale/contacts from range/cone/items.
let rdrData = { present: false, range: 0, cone: 0, metric: false, radarOn: false, levelTime: 0, hdg: 0, items: [], pb: [] };

// Latest TGT filter state, mirrored from the map iframe's SSE feed. The shell keeps only this
// state and forwards it to the frame; the page renders the toggles + POSTs the tgt.* commands.
let tgtData = { present: false };

// Latest BDF faction-forces state, mirrored from the map iframe's SSE feed (docs/bdf-page.md).
// The shell keeps only this state and forwards it to the frame or the pane showing it.
let bdfData = { present: false };
// Same, for PAL — the PRIMEVA faction's panel (docs/bdf-page.md).
let palData = { present: false };

// MIS mission-info panel (docs/mdt-pages.md), mirrored the same way.
let misData = { present: false };
// OBJ active-objectives list (docs/mdt-pages.md), mirrored the same way.
let objData = { present: false };

// AKF advanced kill feed (docs/akf-page.md), mirrored the same way — no present:false gate (an
// empty session just reads as all-zero).
let akfData = { all: [], player: [], kills: { aircraft: 0, ship: 0, vehicle: 0, building: 0 },
                 value: 0, fundsGained: 0, fundsSpent: 0 };

// WPT waypoints/routes readout (issue #38) — the widened 'mapinfo' slice (mission/grid/x/z/hdg/
// ox/oy), mirrored the same way as OBJ/MIS. Unlike those, WPT isn't the only consumer — the map
// page itself derives the same values straight from its own frame — but WPT is a separate
// document/iframe, so it needs its own copy forwarded the same way every other page's data is.
let mapInfoData = { mission: null, grid: null, x: null, z: null, hdg: null, ox: null, oy: null };

function clearKeyActions() {
  // Only the page-dynamic banks (left/right) get cleared between pages. The top and bottom
  // banks hold page-independent controls (fullscreen on top; PIN, SWAP, layout… on bottom)
  // whose actions are wired once at startup and must survive page switches.
  ['left', 'right'].forEach(function(bank) {
    keyBanks[bank].forEach(function(k) {
      delete k.dataset.action;
      delete k.dataset.pane;     // split-mode tag; harmless to clear unconditionally
      delete k.dataset.wname;    // weapon.select name (WPN page); clear so it never lingers
      delete k.dataset.group;    // avn.toggle group (AVN page); clear so it never lingers
    });
  });
}

// PREV/NEXT actions across every paginated list (WPN's, MAIN's, AVN's) — bordered
// (.overlay-item.paging) so a paging control reads as distinct from a destination label, in both
// full view and split.
const PAGING_ACTIONS = { 'wpn-prev': true, 'wpn-next': true, 'main-prev': true, 'main-next': true,
                          'avn-prev': true, 'avn-next': true, 'map-nav-prev': true, 'map-nav-next': true };

// `mark` lights the label in the engaged amber — only LAYOUT's current item uses it; every other
// label names a page rather than a state.
function placeOverlayLabel(bankName, keyIndex, label, action, mark) {
  const side = bankName || 'left';
  const bank = keyBanks[side];
  const k = bank && bank[keyIndex];
  if (!k) return null;

  if (action) k.dataset.action = action;
  const el = document.createElement('div');
  el.className = 'overlay-item ' + side + (mark ? ' on' : '') + (PAGING_ACTIONS[action] ? ' paging' : '');
  el.textContent = label;
  el.dataset.key = side + keyIndex;   // ties the label to its physical key, so the SOI cursor can mark it

  const oRect = overlayEl.getBoundingClientRect();
  const kr = k.getBoundingClientRect();
  if (side === 'top' || side === 'bottom') {
    el.style.left = (kr.left + kr.width / 2 - oRect.left) + 'px';
    el.style.top = (side === 'top' ? 16 : oRect.height - 16) + 'px';
  } else {
    el.style.top = (kr.top + kr.height / 2 - oRect.top) + 'px';
  }
  overlayEl.appendChild(el);
  return el;
}

// Render a page: set the overlay background, (re)assign key actions, and position
// each item label next to its physical key.
function showPage(name) {
  currentPage = name;
  overlayEl.classList.toggle('opaque', !!OPAQUE_PAGES[name]);
  // Stand the MAIN label up for pages with their own content in the top-left (TGT's RESET FILTER,
  // HUD's mode/category rows, BDF's WARHEADS readout), so a horizontal label doesn't cover it.
  // See .overlay.vmain in mfd.css and isVmainPage below.
  overlayEl.classList.toggle('vmain', isVmainPage(name));
  infoBox.classList.toggle('show', name === 'main');
  screenEl.classList.toggle('page-on', !!FRAME_PAGES[name]);   // WPN/TGT/TGP/AVN render in #page-frame
  clearKeyActions();
  // Only wipe dynamic line-select labels (+ WPN's purely-decorative MASTER/MODE and MAP's ZOOM
  // labels, docs/radar-master-arms.md, issue #41); static children (info-box) stay put.
  overlayEl.querySelectorAll('.overlay-item, .wpn-decor').forEach(function(el) { el.remove(); });

  if (name === 'main') {
    // MAIN_SPLIT_ITEMS — all eleven destinations, alphabetically — rather than NAV.main +
    // BEZEL_EXTRAS.main separately: full view has room for all of them at once (six left-bank keys,
    // then the right bank), so it's the same ordering a split pane pages through, just unpaginated.
    MAIN_SPLIT_ITEMS.forEach(function (item, i) {
      const bank = i < 6 ? 'left' : 'right';
      placeOverlayLabel(bank, i < 6 ? i : i - 6, item.label, item.action);
    });
  } else if (name === 'map') {
    // MAP_FULL_LEFT/RIGHT (issue #38 follow-up), not the generic NAV sweep — see their own comment.
    MAP_FULL_LEFT.forEach(function (item, i) { placeOverlayLabel('left', i, item.label, item.action); });
    // rt-next/rt-prev only show — and so only work, since clearKeyActions already wiped every key's
    // action and nothing reassigns a skipped one's — while at least one route is saved (deactivate
    // follow-up: they still work with none ACTIVE, cycling into one). wpt-next/wpt-prev need a route
    // actually active, since they step ITS next waypoint. WPT (index 0) always shows regardless.
    const hasRoutes = WaypointsStore.load().routes.length > 0;
    const hasActiveRoute = !!WaypointsStore.getActiveRoute();
    mapFullRight(hasRoutes, hasActiveRoute).forEach(function (item, i) { placeOverlayLabel('right', i, item.label, item.action); });
    placeMapDecorators({ bank: 'left', index: 3 });                  // ZOOM between Z+/Z- (left3/left4)
    if (hasRoutes) {
      placeMapRouteDecorator({ bank: 'right', index: 1 });           // ROUTE between R+/R- (right1/right2)
    }
    if (hasActiveRoute) {
      placeMapWptDecorator({ bank: 'right', index: 3 });             // WYPT between W+/W- (right3/right4)
    }
  } else {
    // Bezel full-view rendering of the navigation model: item i → left-column key i. `mark` lights
    // an item active (e.g. NAV.bdf/NAV.pal flagging whichever of BDF/PAL is the current page).
    (NAV[name] || []).forEach(function(item, i) {
      const m = fullViewSlot(i);
      placeOverlayLabel(m.bank, m.index, item.label, item.action, item.mark);
    });
    // ...then this layout's own, which name their own key (see BEZEL_EXTRAS). Only full view runs
    // through here, so LYT never appears on a split pane's MAIN — which is what "full-view only"
    // means, with nothing to enforce it.
    (BEZEL_EXTRAS[name] || []).forEach(function(item) {
      placeOverlayLabel(item.bank, item.index, item.label, item.action, item.mark);
    });
  }

  // RDR's twin — RANGE decorator between R+/R-. R+ is NAV.rdr[1], so fullViewSlot(1) is its
  // physical key in full view (left1).
  if (name === 'rdr') placeRdrDecorators(fullViewSlot(1));

  // WPN owns its own nav labels (PREV/MAIN + NEXT) because they depend on the page state; run
  // after the generic label sweep so they don't get clobbered. It renders in #page-frame: point
  // the frame at the page (switching src as needed) then forward layout + data.
  if (name === 'wpn') {
    showFramePage('wpn');
    placeWpnNavLabels();                                          // MAIN/PREV/NEXT (shell-owned)
    forwardWpnLayoutToFrame(); forwardWpnToFrame(); forwardCmToFrame();
  }
  // TGP renders in #page-frame too. Its only key is the static MAIN label (NAV.tgp,
  // placed by the generic sweep above), so there's no extra nav wiring — just forward the
  // lock flag. The page connects to /tgp.mjpg itself once loaded.
  if (name === 'tgp') {
    showFramePage('tgp');
    forwardTgpToFrame();
  }
  // AVN renders in #page-frame too. Its MAIN label (NAV.avn) is placed by the generic sweep
  // above; the 8 avn.toggle groups get their own keys + NAV labels here (placeAvnNavLabels), then
  // forward the bezel geometry (full profile) + snapshot.
  if (name === 'avn') {
    showFramePage('avn');
    placeAvnNavLabels();
    forwardAvnLayoutToFrame(); forwardAvnToFrame();
  }
  // AFM renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.afm, placed
  // by the generic sweep above); forward the bezel geometry (full profile) + snapshot.
  if (name === 'afm') {
    showFramePage('afm');
    forwardAfmLayoutToFrame(); forwardAfmToFrame();
  }
  // RWR renders in #page-frame too. Its only key is the static MAIN label (NAV.rwr,
  // placed by the generic sweep above); forward the contact + missile snapshots.
  if (name === 'rwr') {
    showFramePage('rwr');
    forwardRwrToFrame(); forwardMwToFrame();
  }
  // RDR renders in #page-frame too (docs/rdr-page.md). Its only bezel key is the static MAIN label
  // (NAV.rdr, placed by the generic sweep above); the PAD cursor + lock work inside the page.
  if (name === 'rdr') {
    showFramePage('rdr');
    forwardRdrToFrame();
  }
  // TGT renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.tgt,
  // placed by the generic sweep above); everything else is clickable in the page. Forward state.
  if (name === 'tgt') {
    showFramePage('tgt');
    forwardTgtToFrame();
    forwardTgtTargetsToFrame();
  }
  // AKF renders in #page-frame too — same MDT family as BDF/PAL/MIS/OBJ (NAV.akf marks AKF instead).
  if (name === 'akf') {
    showFramePage('akf');
    forwardAkfToFrame();
  }
  // BDF renders in #page-frame too. Its bezel keys are MAIN/AKF/MIS/OBJ/BDF/PAL (NAV.bdf, placed by
  // the generic sweep above, `mark` lighting BDF since this is that page) — reached from MAIN via
  // MDT (BEZEL_EXTRAS.main, action 'akf' lands on AKF instead) and carried into a split via
  // SPLIT_SLOTS.bdf. Forward state.
  if (name === 'bdf') {
    showFramePage('bdf');
    forwardBdfToFrame();
  }
  // PAL renders in #page-frame too — same as BDF (NAV.pal marks PAL instead), for the PRIMEVA block.
  if (name === 'pal') {
    showFramePage('pal');
    forwardPalToFrame();
  }
  // MIS renders in #page-frame too — same MDT family as BDF/PAL (NAV.mis marks MIS instead).
  if (name === 'mis') {
    showFramePage('mis');
    forwardMisToFrame();
  }
  // OBJ renders in #page-frame too — same MDT family (NAV.obj marks OBJ instead).
  if (name === 'obj') {
    showFramePage('obj');
    forwardObjToFrame();
  }
  // HUD renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.hud, placed by
  // the generic sweep above); the page is otherwise self-driven — it fetches /hud-options and POSTs
  // its own hud.* commands, so the shell forwards it nothing.
  if (name === 'hud') showFramePage('hud');
  // KEY (extended keybinds) renders in #page-frame too. Like HUD it's self-driven — it polls
  // /keybinds-config and POSTs its own keybind.* commands — so the shell forwards it nothing.
  if (name === 'keys') showFramePage('keys');
  // RTS (cfg-rates experiment, issue #39) renders in #page-frame too — same self-driven shape as
  // KEY/HUD: it polls /rates-config and POSTs its own rates.set commands.
  if (name === 'rates') showFramePage('rates');
  // WPT (issue #38) renders in #page-frame too — unlike KEY/RTS it isn't self-driven: it needs the
  // mapinfo slice (own-ship position/heading + map meta) forwarded for its readout.
  if (name === 'wpt') {
    showFramePage('wpt');
    forwardWptToFrame();
  }

  // refreshFollowIndicator (not just renderIndicators) because the FOLLOW chip's membership
  // depends on currentPage, which just changed: entering MAP with follow already on must add the
  // chip now (the map's follow state was reported earlier, while another page was in view), and
  // leaving MAP must drop it. It renders the full indicator stack (incl. PINNED) internally.
  refreshFollowIndicator();
  renderSoiCursor();   // the labels were just rebuilt; re-mark the cursored one (and clamp it)
  syncCursorFocus();   // full view just navigated onto/off MAP — the focused surface may be it
}

// The map iframe broadcasts status + loadout + cm via postMessage; mirror onto the
// info-box (MAIN page), the cached wpnData + cmData (WPN page).
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  // Telemetry-mirror messages come only from the canonical map iframe (mapFrame). In split mode
  // a MAP *pane* is a second map iframe that also streams to the shell; ignoring its duplicate
  // data posts keeps the RWR/AVN/etc. mirrors on a single source — otherwise two out-of-phase
  // feeds drive them (jumpy in preview, redundant live). 'follow' and 'grid' (issue #41) are both
  // per-pane and route by e.source itself, so they must pass through from any map source — this
  // gate dropping 'grid' silently left a split pane's GRID label stuck unlit forever, even though
  // the pane's own map correctly persisted and drew the grid regardless.
  if (m.type !== 'follow' && m.type !== 'grid' && e.source !== mapFrame.contentWindow) return;
  if (m.type === 'status') {
    lastStatusCls  = m.cls;
    lastStatusText = m.text;
    ibStatus.className = 'ib-status mfd-status ' + m.cls;
    ibStatus.textContent = m.text;
    if (splitMode) forwardStatusToPanes();
  } else if (m.type === 'soi-cid') {
    // The tap learned this instance's cid — remember it and report the surface count under it (this
    // also fires after an SSE reconnect, when the server has reset the count to 1).
    myCid = m.cid || '';
    reportPanes();
    // Same cue for the in-game waypoint bug (docs/hud-waypoint-indicator.md): a fresh server
    // connection may be a fresh game process, which would have lost the published waypoint.
    WaypointsStore.republishActive();
  } else if (m.type === 'soi') {
    // Which of this instance's surfaces (if any) is the sensor of interest. Reported by the tap, the
    // only part that knows this instance's cid. Ring frames the focused pane; the cursor scopes to
    // it. Moving to a different surface (or losing focus) drops the cursor — the destination reveals
    // it fresh on the next NAV, like a first focus.
    const prevPane = soiPane;
    soiPane = m.focused ? (typeof m.pane === 'number' ? m.pane : 0) : -1;
    if (soiPane !== prevPane) setSoiCursor(-1);
    positionSoiRing();
    syncCursorFocus();   // the focused surface itself changed — re-evaluate who owns the map cursor
  } else if (m.type === 'soi-act') {
    soiAct(m.act);
  } else if (m.type === 'cursor') {
    // docs/page-cursor.md, docs/map-cursor.md — forward straight to whichever iframe is both the
    // focused surface AND currently showing a PAD-cursor-eligible page (focusedCursorWindow
    // returns null otherwise, so this is a safe no-op).
    const w = focusedCursorWindow();
    if (w) w.postMessage({ mfd: true, action: 'cursor', x: m.x || 0, y: m.y || 0 }, '*');
  } else if (m.type === 'cursor-select') {
    const w = focusedCursorWindow();
    if (w) w.postMessage({ mfd: true, action: 'cursor-select' }, '*');
  } else if (m.type === 'cursor-held') {
    // docs/page-cursor.md — Cursor Select's live held state, for a page (TGT) that tells a tap from
    // a hold. MAP ignores this action; only pages that opt in (pass onHold to createPadCursor) do.
    const w = focusedCursorWindow();
    if (w) w.postMessage({ mfd: true, action: 'cursor-held', held: !!m.held }, '*');
  } else if (m.type === 'map-act') {
    // Follow/Zoom In/Zoom Out — MAP interprets these as view controls; TGT repurposes Zoom In/Out
    // to scroll its target list (docs/page-cursor.md), HUD has nothing to do with them yet, so
    // both are simply inert there. Routed the same way cursor/cursor-select are: straight to
    // whichever surface is focused AND showing a PAD-cursor-eligible page.
    const w = focusedCursorWindow();
    if (w) w.postMessage({ mfd: true, action: m.act }, '*');
  } else if (m.type === 'loadout') {
    const prevSel = wpnData.selWeapon;
    wpnData = { items: m.items || [], selWeapon: m.selWeapon || null,
                softGun: m.softGun || null, softRel: m.softRel || null,
                masterArmsOn: m.masterArmsOn !== false, combatMode: m.combatMode || 'all' };
    const selChanged = wpnData.selWeapon && wpnData.selWeapon !== prevSel;
    // Full-view: follow the in-game selection to its page when it moves off the current page.
    // Only on an actual change, so manual paging is preserved on ammo/loadout ticks.
    if (selChanged) {
      const p = selWpnPageFull();
      if (p >= 0) wpnPage = p;
    }
    // Full-view: re-forward the slice to the frame + refresh the nav labels (loadout change
    // can add/remove pages, changing PREV/NEXT visibility).
    if (currentPage === 'wpn' && !splitMode) { forwardWpnToFrame(); placeWpnNavLabels(); }
    // Loadout change can add/remove pages, so refresh the panes' slices + NEXT/PREV labels.
    if (splitMode) {
      // Split-pane twin of the above: jump each visible WPN pane to the selection's page.
      if (selChanged) autoPageToSelection();
      forwardWpnToPanes();
      renderSplitLabels();
    }
  } else if (m.type === 'cm') {
    cmData = {
      flares:    typeof m.flares    === 'number' ? m.flares    : -1,
      flaresMax: typeof m.flaresMax === 'number' ? m.flaresMax : -1,
      ewKJ:      typeof m.ewKJ      === 'number' ? m.ewKJ      : -1,
      ewKJMax:   typeof m.ewKJMax   === 'number' ? m.ewKJMax   : -1,
      cmCat:     m.cmCat || 0
    };
    if (currentPage === 'wpn' && !splitMode) forwardCmToFrame();
    if (splitMode) forwardCmToPanes();
  } else if (m.type === 'tgp') {
    tgpActive = !!m.active;
    // Only matters while the TGP page is in view — outside it the frame/pane isn't shown.
    if (currentPage === 'tgp' && !splitMode) forwardTgpToFrame();
    if (splitMode) forwardTgpToPanes();
  } else if (m.type === 'avn') {
    avnData = {
      name: m.name || null,
      parts: Array.isArray(m.parts) ? m.parts : null,
      failures: Array.isArray(m.failures) ? m.failures : null,
      pylons:   Array.isArray(m.pylons)   ? m.pylons   : null,
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
      heat:     typeof m.heat     === 'number' ? m.heat     : -1,
      heatColor: typeof m.heatColor === 'string' ? m.heatColor : null,
      rpm:      typeof m.rpm      === 'number' ? m.rpm      : -1,
      hasAb:    m.hasAb === true,
      abStart:  typeof m.abStart === 'number' ? m.abStart : 1,
      gearDown: m.gearDown === true,
      radar:    m.radar    === true,
      guns:     m.guns     === true,
      ignition: m.ignition === true,
      assist:   m.assist   === true,
      turret:   m.turret   === true,
      nvg:      m.nvg      === true,
      navLights: m.navLights === true,
    };
    // AVN renders in the #page-frame iframe (full) or a pane (split); forward the snapshot.
    if (currentPage === 'avn' && !splitMode) forwardAvnToFrame();
    if (splitMode) { forwardAvnToPanes(); forwardAvnLayoutToPanes(); }
    // AFM shares this same snapshot (name/parts/failures) for its silhouette.
    if (currentPage === 'afm' && !splitMode) forwardAfmToFrame();
    if (splitMode) forwardAfmToPanes();
  } else if (m.type === 'follow') {
    // Map iframe broadcasts its follow state on toggle / mission clear. Route by source: the
    // canonical full-view map drives single-mode follow; each split MAP pane drives its own.
    // The FOLLOW chip (refreshFollowIndicator → followActive) reflects whichever map context
    // is currently visible, so it lives in the same stack as PINNED.
    const on = !!m.on;
    if      (e.source === mapFrame.contentWindow)       followOn = on;
    else if (e.source === paneIframes[0].contentWindow) paneFollowOn[0] = on;
    else if (e.source === paneIframes[1].contentWindow) paneFollowOn[1] = on;
    else return;
    refreshFollowIndicator();
  } else if (m.type === 'grid') {
    // Map iframe broadcasts its grid-overlay state on toggle / mission clear, same protocol as
    // 'follow' above (issue #41) — routed by source, one state per map context.
    const on = !!m.on;
    if      (e.source === mapFrame.contentWindow)       gridOn = on;
    else if (e.source === paneIframes[0].contentWindow) paneGridOn[0] = on;
    else if (e.source === paneIframes[1].contentWindow) paneGridOn[1] = on;
    else return;
    refreshFollowIndicator();
  } else if (m.type === 'targets') {
    // Mirror the selected-target list; the TGT page renders it under its filters.
    targetsData = { targets: Array.isArray(m.items) ? m.items : [] };
    if (currentPage === 'tgt' && !splitMode) forwardTgtTargetsToFrame();
    if (splitMode) forwardTgtTargetsToPanes();
  } else if (m.type === 'rwr') {
    // Mirror the radar-warning emitters (already nose-up plot data from ClientPage) for the RWR
    // scope, which renders in the #page-frame iframe (full) or a pane (split); forward it on.
    rwrData = { items: Array.isArray(m.items) ? m.items : [] };
    if (currentPage === 'rwr' && !splitMode) forwardRwrToFrame();
    if (splitMode) forwardRwrToPanes();
  } else if (m.type === 'mw') {
    // Mirror incoming missiles for the RWR's launch indicator (same plumbing as 'rwr').
    mwData = { items: Array.isArray(m.items) ? m.items : [] };
    if (currentPage === 'rwr' && !splitMode) forwardMwToFrame();
    if (splitMode) forwardMwToPanes();
  } else if (m.type === 'rdr') {
    // Mirror the RDR B-scope block (own-radar air contacts, already nose-up from ClientPage) and
    // forward it to whichever surface shows RDR. See docs/rdr-page.md.
    rdrData = { present: !!m.present, range: m.range || 0, cone: m.cone || 0, metric: !!m.metric,
                radarOn: !!m.radarOn, levelTime: m.levelTime || 0, hdg: m.hdg || 0,
                items: Array.isArray(m.items) ? m.items : [],
                pb: Array.isArray(m.pb) ? m.pb : [] };
    if (currentPage === 'rdr' && !splitMode) forwardRdrToFrame();
    if (splitMode) forwardRdrToPanes();
  } else if (m.type === 'tgt') {
    // Mirror the TGT filter state (present + toggle groups). Renders in the #page-frame iframe (full)
    // or a pane (split); forward on when it's the page in view.
    tgtData = m;
    if (currentPage === 'tgt' && !splitMode) forwardTgtToFrame();
    if (splitMode) forwardTgtToPanes();
  } else if (m.type === 'bdf') {
    // Mirror the BDF faction-forces state. Renders in the #page-frame iframe (full) or a pane
    // (split); forward on when it's the page in view.
    bdfData = m;
    if (currentPage === 'bdf' && !splitMode) forwardBdfToFrame();
    if (splitMode) forwardBdfToPanes();
  } else if (m.type === 'pal') {
    // Same as 'bdf', for the PRIMEVA block.
    palData = m;
    if (currentPage === 'pal' && !splitMode) forwardPalToFrame();
    if (splitMode) forwardPalToPanes();
  } else if (m.type === 'mis') {
    // Mirror the MIS mission-info block. Renders in the #page-frame iframe (full) or a pane
    // (split); forward on when it's the page in view.
    misData = m;
    if (currentPage === 'mis' && !splitMode) forwardMisToFrame();
    if (splitMode) forwardMisToPanes();
  } else if (m.type === 'obj') {
    // Mirror the OBJ active-objectives list, same forwarding shape as MIS.
    objData = m;
    if (currentPage === 'obj' && !splitMode) forwardObjToFrame();
    if (splitMode) forwardObjToPanes();
  } else if (m.type === 'akf') {
    // Mirror the AKF kill-feed/session-stats block, same forwarding shape as MIS/OBJ.
    akfData = m;
    if (currentPage === 'akf' && !splitMode) forwardAkfToFrame();
    if (splitMode) forwardAkfToPanes();
  } else if (m.type === 'mapinfo') {
    // Mirror the mapinfo slice for WPT (issue #38), same forwarding shape as MIS/OBJ.
    mapInfoData = m;
    if (currentPage === 'wpt' && !splitMode) forwardWptToFrame();
    if (splitMode) forwardWptToPanes();
  }
});

// Drive the map iframe without reaching into it (keeps the map a standalone component;
// also works cross-origin under file://).
function mapSend(action) {
  if (mapFrame && mapFrame.contentWindow)
    mapFrame.contentWindow.postMessage({ mfd: true, action: action }, '*');
}

// Replay the CRT boot flicker (≤2s, capped by the 1.6s animation). Re-arming requires
// clearing the class and forcing a reflow so the animation restarts from 0% each time.
function flickerScreen() {
  screenEl.classList.remove('powering-on');
  void screenEl.offsetWidth;                 // reflow — restart the animation
  screenEl.classList.add('powering-on');
  setTimeout(function() { screenEl.classList.remove('powering-on'); }, 1100);
}

// Boot loader for the centre info box: shows a LOADING… line + a fill bar, keeps the title
// visible, hides the data rows until the bar hits 100%. Runs alongside the first-load
// boot flicker. Fills in 5% steps every 50ms = 1.0s (the 60ms CSS transition on the
// fill smooths each step into a continuous sweep, like the EW Jammer bar).
let bootTimer = null;
function runBootLoading() {
  const fill = document.getElementById('ib-bar-fill');
  if (!infoBox || !fill) return;
  if (bootTimer) { clearInterval(bootTimer); bootTimer = null; }
  let pct = 0;
  infoBox.classList.add('booting');            // CSS swaps data rows → loading block
  fill.style.width = '0%';
  bootTimer = setInterval(function() {
    pct += 5;
    fill.style.width = Math.min(pct, 100) + '%';
    if (pct >= 100) {
      clearInterval(bootTimer); bootTimer = null;
      infoBox.classList.remove('booting');     // reveal the data rows
      typewriterUrls();                        // then type the URL lines out
    }
  }, 50);
}

// Type the info box's URL line(s) out character-by-character with a blinking cursor, one
// line after the other. Called once the boot loader reveals the data. Each line keeps its
// FULL text laid out the whole time — a visible "done" prefix + an invisible "rest" suffix
// (visibility:hidden, so it still reserves space) — so neither width nor height shifts as
// the text appears, and a not-yet-typed line stays blank-but-reserved. We also freeze
// .ib-body's width as belt-and-suspenders (the cursor glyph aside). A token guards against
// overlap if another boot starts mid-type.
let twToken = 0;
function typewriterUrls() {
  if (!infoBox) return;
  const body  = infoBox.querySelector('.ib-body');
  const lines = [].slice.call(infoBox.querySelectorAll('.ib-data .ib-url'));
  if (!body || !lines.length) return;
  const myToken = ++twToken;
  // Pin the body to a FIXED width (not just a floor) for the duration, so neither the cursor
  // nor sub-pixel kerning at the typed/untyped split can nudge the centred box's frame.
  body.style.width = body.getBoundingClientRect().width + 'px';
  // Set every line up front: full text in the hidden 'rest', nothing typed, cursor parked.
  lines.forEach(function(el) {
    // Cache the ORIGINAL text once. A re-run (new boot superseding an in-flight type) must
    // read this, not the cursor/partial spans a prior run left in the DOM.
    if (el.dataset.url === undefined) el.dataset.url = el.textContent;
    const full = el.dataset.url;
    el.textContent = '';
    const done = document.createElement('span'); done.className = 'tw-done';
    const cur  = document.createElement('span'); cur.className  = 'tw-cursor'; cur.textContent = '▌'; cur.style.display = 'none';
    const rest = document.createElement('span'); rest.className = 'tw-rest';  rest.textContent = full;
    el.appendChild(done); el.appendChild(cur); el.appendChild(rest);
  });
  function typeLine(idx) {
    if (myToken !== twToken) return;                 // superseded by a newer boot
    if (idx >= lines.length) { body.style.width = ''; return; }
    const el = lines[idx];
    const done = el.children[0], cur = el.children[1], rest = el.children[2];
    const full = rest.textContent;
    cur.style.display = '';                           // reveal the blinking cursor on this line
    let i = 0;
    const timer = setInterval(function() {
      if (myToken !== twToken) { clearInterval(timer); return; }
      i++;
      done.textContent = full.slice(0, i);
      rest.textContent = full.slice(i);
      if (i >= full.length) {
        clearInterval(timer);
        el.textContent = full;                       // collapse spans back to plain text
        typeLine(idx + 1);                           // chain to the next line
      }
    }, 32);
  }
  typeLine(0);
}

function setInfoUrls(cfg) {
  if (!infoBox) return;
  const status = document.getElementById('ib-status');
  if (!status || !status.parentNode) return;

  const urls = [cfg && cfg.localhost ? cfg.localhost : 'http://localhost:5005'];
  if (cfg && cfg.lanUrl) urls.push(cfg.lanUrl);

  [].slice.call(infoBox.querySelectorAll('.ib-data .ib-url')).forEach(function(el) { el.remove(); });
  urls.forEach(function(url) {
    const el = document.createElement('div');
    el.className = 'ib-url';
    el.textContent = url;
    status.parentNode.insertBefore(el, status);
  });

  // If /config arrives after the boot loader has revealed the rows, replay the typewriter
  // against the fresh URL nodes. During boot, runBootLoading() will call typewriterUrls().
  if (!infoBox.classList.contains('booting')) typewriterUrls();
}

function loadConfigUrls() {
  fetch('/config', { cache: 'no-store' })
    .then(function(r) { if (!r.ok) throw new Error('config'); return r.json(); })
    .then(setInfoUrls)
    .catch(function() {});
}

// sendCommand(cmd, args) — POST /command — is provided by src/web/services/send-command.js
// (linked before this script in mfd.html). State changes (e.g. a deselected target dropping off
// the target list) come back via normal telemetry, so the shell's calls are fire-and-forget: add
// .catch() at the call site since the shared sender returns the raw promise.

// ── SOI cursor ───────────────────────────────────────────────────────────────────────
// NAV UP/DOWN walk a cursor over this display's line-select keys and SELECT presses the one it
// stops on — reaching out and touching a bezel key, for a screen you are not touching. Only ever
// driven while this display is the SOI; the tap forwards nothing otherwise (docs/keybinds-page.md).
//
// The list is DERIVED, never maintained: everything navigable in this shell is already a label on a
// physical key carrying a data-action, and mfdButton() is already the single activation entry. So
// the cursor needs no per-page knowledge and every page with keys gets it at once — MAIN's menu,
// WPN's weapon list and paging, MAP's zoom rocker, LYT. Pages whose controls live inside their
// iframe (TGT, HUD) expose only their MAIN key here; operating those needs a page-level cursor
// protocol, which is the MVP limit the doc names.
//
// Left bank then right, top to bottom — how the bezel reads. The top/bottom banks stay out: they
// are fixed chrome (fullscreen, PIN, the split presets), not page navigation.
//
// Focus is a SURFACE, not the whole display (docs/keybinds-page.md): full view is one surface, a
// split is two panes. soiPane says which of THIS instance's surfaces is focused (-1 = none), so the
// cursor scopes to that pane's keys and the ring frames just it — SOI NEXT steps top→bottom→next
// display, and the cursor no longer spans both panes.
let soiCursor = -1;   // index into soiKeys(); -1 = no cursor
let soiPane   = -1;   // focused surface index of this instance, from the tap; -1 = not the SOI
let myCid     = '';   // this instance's cid, for soi.panes reports (from the tap's soi-cid)

// Report how many focusable surfaces this display shows now — 1 in full view, 2 in a split — so the
// server cycles surfaces, not whole documents. Needs the cid, which arrives from the tap; until then
// this no-ops and the soi-cid handler re-invokes it. Re-sent on every split change and reconnect.
function reportPanes() {
  if (!myCid) return;
  sendCommand('soi.panes', { cid: myCid, n: splitMode ? 2 : 1 }).catch(function() {});
}

// ── PAD cursor forwarding (docs/page-cursor.md, docs/map-cursor.md) ───────────────────
// Pages that carry their own PAD cursor (pad-cursor.js) — MAP's canvas crosshair, and TGT/HUD/RDR/
// WPT's DOM-hit-test cursor. BDF/PAL stays out: read-only, nothing to click (docs/page-cursor.md).
const PAD_CURSOR_PAGES = { map: true, tgt: true, hud: true, rdr: true, wpt: true };

// The focused surface is drivable as a PAD cursor only while it's actually SHOWING an eligible
// page — the SOI ring/bezel-key cursor above frames "the recess," but the cursor needs the real
// content check (a focused pane can page onto/off an eligible page without soiPane itself
// changing). null = nothing eligible. MAP renders in its own always-alive mapFrame in full view;
// every other eligible page (TGT/HUD) renders in the shared #page-frame.
function focusedCursorWindow() {
  if (soiPane < 0) return null;
  if (splitMode) {
    const page = panePages[soiPane];
    return PAD_CURSOR_PAGES[page] ? paneIframes[soiPane].contentWindow : null;
  }
  if (!PAD_CURSOR_PAGES[currentPage]) return null;
  return currentPage === 'map' ? mapFrame.contentWindow : pageFrame.contentWindow;
}

// Tell the iframe that just lost eligibility to drop its cursor, and the one that just gained
// it to show one — called after anything that could change the answer (focus moves, page
// navigation under the focused surface, split toggling). No-ops when the answer didn't change.
let cursorFocusTarget = null;   // the iframe window currently holding cursor focus, or null
function syncCursorFocus() {
  const target = focusedCursorWindow();
  if (target === cursorFocusTarget) return;
  if (cursorFocusTarget) cursorFocusTarget.postMessage({ mfd: true, action: 'cursor-focus', on: false }, '*');
  if (target) target.postMessage({ mfd: true, action: 'cursor-focus', on: true }, '*');
  cursorFocusTarget = target;
}

// Position the SOI ring over the focused surface: the whole recess in full view (the map iframe
// fills it), one pane's box in a split. Measured rather than CSS-placed so a flex-sized pane
// (V_WIDE is 2:1) is framed exactly. Hidden when this display isn't the SOI.
function positionSoiRing() {
  if (soiPane < 0) { soiRingEl.style.display = 'none'; return; }
  const target = splitMode ? paneIframes[soiPane === 1 ? 1 : 0] : mapFrame;
  if (!target) { soiRingEl.style.display = 'none'; return; }
  const s = screenEl.getBoundingClientRect();
  const t = target.getBoundingClientRect();
  soiRingEl.style.left   = (t.left - s.left) + 'px';
  soiRingEl.style.top    = (t.top  - s.top ) + 'px';
  soiRingEl.style.width  = t.width  + 'px';
  soiRingEl.style.height = t.height + 'px';
  soiRingEl.style.display = 'block';
}

// The focused surface's keys. In a split, only the focused pane's — each split key carries its
// data-pane, so the cursor stops spanning both panes. In full view (one surface) it's every key.
function soiKeys() {
  const out = [];
  const paneTag = splitMode ? (soiPane === 1 ? 'bot' : 'top') : null;
  ['left', 'right'].forEach(function(side) {
    keyBanks[side].forEach(function(k) {
      if (!k.dataset.action) return;
      if (paneTag && k.dataset.pane !== paneTag) return;
      out.push(k);
    });
  });
  return out;
}

// Paint the cursor on the KEY, and on its label when it has one. Marking only the label would lose
// the cursor exactly where it matters most: WPN's weapon rows are keys with an action whose text is
// drawn inside the page iframe, so no overlay label exists to mark. Every action key has a key.
//
// Re-run after anything that rebuilds labels — they are thrown away and recreated on each page
// change. The index is CLAMPED rather than reset, so paging (WPN's PREV/NEXT, MAIN's) keeps the
// cursor roughly where it was instead of snapping back to the top on every press.
function renderSoiCursor() {
  overlayEl.querySelectorAll('.overlay-item.cursor').forEach(function(el) { el.classList.remove('cursor'); });
  ['left', 'right'].forEach(function(side) {
    keyBanks[side].forEach(function(k) { k.classList.remove('cursor'); });
  });
  if (soiCursor < 0) return;
  const keys = soiKeys();
  if (!keys.length) { soiCursor = -1; return; }
  if (soiCursor >= keys.length) soiCursor = keys.length - 1;
  const k = keys[soiCursor];
  k.classList.add('cursor');
  const el = overlayEl.querySelector('.overlay-item[data-key="' + k.dataset.pos + '"]');
  if (el) el.classList.add('cursor');
}

function setSoiCursor(i) { soiCursor = i; renderSoiCursor(); }

function soiAct(act) {
  const keys = soiKeys();
  if (!keys.length) return;

  if (act === 'select') {
    if (soiCursor >= 0 && soiCursor < keys.length) {
      // A SELECT that navigates to a new screen drops the cursor, so the destination shows nothing
      // highlighted until the pilot summons it again with NAV — landing pre-parked on the new page's
      // first key (usually MAIN) reads as a selection nobody made. A SELECT that stays put (a weapon
      // pick, a page-turn) keeps the cursor, so you can act again without re-summoning it. The nav
      // signature covers both full view (currentPage) and a split pane (panePages).
      const navBefore = currentPage + '|' + panePages.join(',');
      mfdButton(keys[soiCursor]);
      if (currentPage + '|' + panePages.join(',') !== navBefore) { setSoiCursor(-1); return; }
    }
    renderSoiCursor();   // stayed put: re-apply the mark to the (possibly rebuilt) label set
    return;
  }

  const dir = act === 'up' ? -1 : 1;
  // The first NAV press only reveals the cursor — landing it somewhere unannounced and moving it in
  // the same press would make the first press of a session unpredictable. Entering from the end the
  // key came from, as SOI NEXT/PREV do from no focus.
  if (soiCursor < 0) setSoiCursor(dir > 0 ? 0 : keys.length - 1);
  else setSoiCursor(((soiCursor + dir) % keys.length + keys.length) % keys.length);
}

function mfdButton(el) {
  el.classList.add('lit');                                   // brief press feedback
  setTimeout(function() { el.classList.remove('lit'); }, 150);

  // Split-mode line-select keys carry a data-pane tag (top/bot). The action on
  // them names a destination page; clicking navigates ONLY that pane.
  if (splitMode && el.dataset.pane && el.dataset.action) {
    const paneIdx = el.dataset.pane === 'top' ? 0 : 1;
    const act = el.dataset.action;
    // WPN paging stays within the pane — bump its page index and re-send the slice + labels
    // rather than navigating. Everything else is a destination page for that pane.
    if (act === 'wpn-prev' || act === 'wpn-next') {
      paneWpnPage[paneIdx] += (act === 'wpn-next' ? 1 : -1);
      forwardWpnToPanes();
      renderSplitLabels();
    } else if (act === 'avn-prev' || act === 'avn-next') {
      // AVN paging stays within the pane, same idea as WPN's — bump its page index and re-send
      // the visible groups + labels + row geometry rather than navigating.
      paneAvnPage[paneIdx] += (act === 'avn-next' ? 1 : -1);
      forwardAvnToPanes();
      renderSplitLabels();
      forwardAvnLayoutToPanes();
    } else if (act === 'main-prev' || act === 'main-next') {
      // MAIN's own list paging — same idea as WPN's, but bumping paneMainPage (mainPaneSlice).
      paneMainPage[paneIdx] += (act === 'main-next' ? 1 : -1);
      renderSplitLabels();
    } else if (act === 'map-nav-prev' || act === 'map-nav-next') {
      // MAP's own list paging (issue #38's R+/R- pushed NAV.map past a split pane's 6-key budget)
      // — same idea as MAIN's own paging just above, bumping paneMapNavPage (mapNavPaneSlice).
      paneMapNavPage[paneIdx] += (act === 'map-nav-next' ? 1 : -1);
      renderSplitLabels();
    } else if (act === 'lyt') {
      // LYT is a whole-document layout switch, not per-pane content (no PAGE_URL entry) — leaving
      // split is the only sensible destination, same as the 'unsplit' case below but landing on LYT
      // instead of the top/left pane's page.
      splitMode = false;
      currentPage = 'lyt';
      applySplitMode();
    } else if (act === 'flw' || act === 'zin' || act === 'zout' || act === 'grid' || act === 'rt-next' || act === 'rt-prev'
        || act === 'wpt-next' || act === 'wpt-prev') {
      // MAP controls act on the pane's own map iframe — they don't navigate it away.
      paneMapSend(paneIdx, act === 'flw' ? 'toggle-follow' : act === 'zin' ? 'zoom-in' : act === 'zout' ? 'zoom-out'
        : act === 'grid' ? 'toggle-grid' : act === 'rt-next' ? 'route-next' : act === 'rt-prev' ? 'route-prev'
        : act === 'wpt-next' ? 'waypoint-next' : 'waypoint-prev');
    } else if (act === 'rng-in' || act === 'rng-out') {
      // RDR's range rocker acts on the pane's own iframe, same as MAP's zoom above — paneMapSend
      // just posts to whichever iframe is in this pane, not MAP-specific despite the name. Reuses
      // the SAME 'zoom-in'/'zoom-out' action names MAP's zoom sends (issue #40 follow-up), which is
      // also what SOI's Zoom In/Out keybind sends when RDR is the focused surface (docs/page-cursor.md).
      paneMapSend(paneIdx, act === 'rng-in' ? 'zoom-in' : 'zoom-out');
    } else if (act === 'weapon.select') {
      // A weapon row: selection is aircraft-global, not a destination page — same case as the
      // full-view/shared switch below. It carries a data-pane tag only so the SOI cursor (soiKeys())
      // can scope to the focused pane; that tag would otherwise make this branch mistake
      // 'weapon.select' for a page name and hand it to paneNavigate.
      if (el.dataset.wname) sendCommand('weapon.select', { wname: el.dataset.wname }).catch(function() {});
    } else if (act === 'master-arms-on' || act === 'master-arms-off') {
      // Same reasoning as weapon.select above: a mod-state action, not a destination page. Only
      // carries a data-pane tag because it shares WPN's paginated item slots (buildWpnSplitPages).
      sendCommand('master-arms.set', { on: act === 'master-arms-on' }).catch(function() {});
    } else if (act === 'combat-mode-aa' || act === 'combat-mode-ag') {
      sendCommand('combat-mode.set', { group: act === 'combat-mode-aa' ? 'aa' : 'ag' }).catch(function() {});
    } else if (act === 'avn.toggle') {
      // An avionics toggle: mod/game state, not a destination page — same reasoning as
      // weapon.select above. Only carries a data-pane tag so the SOI cursor can scope to it.
      if (el.dataset.group) sendCommand('avn.toggle', { group: el.dataset.group }).catch(function() {});
    } else {
      paneNavigate(paneIdx, act);
    }
    return;
  }

  switch (el.dataset.action) {
    case 'main': showPage('main'); mapSend('status-request'); break;   // pull fresh status on open
    case 'map':  showPage('map');  break;
    case 'wpt':  showPage('wpt');  break;
    case 'wpn':       wpnPage = Math.max(0, selWpnPageFull()); showPage('wpn'); break;   // open on the selected weapon's page
    case 'wpn-prev':  wpnPage--;   showPage('wpn'); break;   // renderWpn clamps on overshoot
    case 'wpn-next':  wpnPage++;   showPage('wpn'); break;
    case 'weapon.select':                                    // WPN bezel key → select the aligned weapon
      if (el.dataset.wname) sendCommand('weapon.select', { wname: el.dataset.wname }).catch(function() {});
      break;
    case 'avn.toggle':                                       // AVN bezel key → toggle the aligned system
      if (el.dataset.group) sendCommand('avn.toggle', { group: el.dataset.group }).catch(function() {});
      break;
    case 'master-arms-on':  sendCommand('master-arms.set', { on: true  }).catch(function() {}); break;
    case 'master-arms-off': sendCommand('master-arms.set', { on: false }).catch(function() {}); break;
    case 'combat-mode-aa':  sendCommand('combat-mode.set', { group: 'aa'  }).catch(function() {}); break;
    case 'combat-mode-ag':  sendCommand('combat-mode.set', { group: 'ag'  }).catch(function() {}); break;
    case 'tgp':  showPage('tgp');  break;
    case 'hud':  showPage('hud');  break;
    case 'keys':  showPage('keys');  break;
    case 'rates': showPage('rates'); break;   // cfg-rates experiment (issue #39) — CFG group's RTS
    case 'lyt':   showPage('lyt');   break;
    // The LAYOUT page's two choices. CLASSIC is this document, so choosing it is just leaving the
    // menu — back to MAIN, where LYT was pressed, with a fresh status as MAIN's own key pulls.
    // F-35 is a different document, so it is a real navigation; that shell lands on its own MAIN.
    // Either choice is remembered (setLayout → localStorage) so a fresh load honors it — the head
    // guard in each shell's HTML redirects on that value (docs/layouts.md, Stage 3).
    case 'lyt-classic': setLayout('classic'); showPage('main'); mapSend('status-request'); break;
    case 'lyt-f35':     setLayout('f35'); location.href = '/f35'; break;
    case 'avn':  showPage('avn');  break;
    case 'afm':  showPage('afm');  break;
    case 'rwr':  showPage('rwr');  break;
    case 'rdr':  showPage('rdr');  break;
    case 'tgt':  showPage('tgt');  break;
    case 'akf':  showPage('akf');  break;
    case 'bdf':  showPage('bdf');  break;
    case 'pal':  showPage('pal');  break;
    case 'mis':  showPage('mis');  break;
    case 'obj':  showPage('obj');  break;
    case 'flw':  mapSend('toggle-follow'); break;
    case 'zin':  mapSend('zoom-in');  break;
    case 'zout': mapSend('zoom-out'); break;
    case 'grid': mapSend('toggle-grid'); break;
    case 'rt-next': mapSend('route-next'); break;   // switch the active waypoint route (issue #38)
    case 'rt-prev': mapSend('route-prev'); break;
    case 'wpt-next': mapSend('waypoint-next'); break;   // manually step the active waypoint (issue #38)
    case 'wpt-prev': mapSend('waypoint-prev'); break;
    // RDR's range rocker — mapSend() targets mapFrame specifically, wrong for RDR (a #page-frame
    // page), so this posts to frameWin() instead. Same 'zoom-in'/'zoom-out' action names as MAP's
    // zin/zout above (issue #40 follow-up): reused, not new, so SOI's Zoom In/Out keybind (which
    // sends the same names — docs/page-cursor.md) drives RDR range for free once RDR is SOI focus.
    case 'rng-in':  { const w = frameWin(); if (w) w.postMessage({ mfd: true, action: 'zoom-in'  }, '*'); } break;
    case 'rng-out': { const w = frameWin(); if (w) w.postMessage({ mfd: true, action: 'zoom-out' }, '*'); } break;
    case 'hide-shell':
      // Collapse the whole shell (frame + strips + side keys) so the screen fills the
      // viewport — for fitting behind a physical MFD frame. Restore button brings it back.
      setShellHidden(true);
      break;
    case 'fll':  toggleFullscreen(); break;
    // Layout presets. Each enters split (carrying the full-view page into the top/left pane,
    // MAIN into the other) or, if already split, switches orientation in place. The square
    // (unsplit) below collapses back to single.
    case 'split':   setSplit('h');  break;   // H_SPLIT — top/bottom
    case 'vsplit':  setSplit('v');  break;   // V_SPLIT — left/right 50/50
    case 'vwsplit': setSplit('vw'); break;   // V_WIDE_SPLIT — left/right 2:1
    case 'unsplit':
      // One-way: collapse split back to single. No-op if already in single mode.
      // The full-screen pane adopts whatever the TOP pane was showing.
      if (!splitMode) break;
      splitMode = false;
      currentPage = panePages[0];
      applySplitMode();
      break;
    case 'swap': {
      // Toggle between the pinned page and the last page we swapped from.
      //   - On a non-pinned page: remember it as the partner, jump to pinned.
      //   - On the pinned page with a known partner: jump back to the partner.
      //   - Otherwise (nothing pinned, or on pinned with no partner yet): no-op.
      // In split mode this drives the top-right pane (paneNavigate) instead of the full stack.
      if (pinnedPage === null) break;
      const tr = splitMode ? topRightPane() : -1;
      const here = splitMode ? panePages[tr] : currentPage;
      const goTo = splitMode ? function(p) { paneNavigate(tr, p); }
                             : function(p) { showPage(p); };
      if (here === pinnedPage) {
        if (swapPartner === null) break;
        goTo(swapPartner);
      } else {
        swapPartner = here;
        goTo(pinnedPage);
      }
      renderIndicators();   // the page in the pinned context changed → PINNED chip visibility follows
      break;
    }
    case 'pin': {
      // Pin/unpin the page in the active context: the top-right pane in split mode, else the
      // full-view page. MENU ('main') is never pinnable.
      const page = splitMode ? panePages[topRightPane()] : currentPage;
      if (page === 'main') break;
      if (pinnedPage === page) { clearPin(); break; }   // toggle off
      // First time on, or switching the pin to a new page: append so we land to the LEFT of any
      // chip activated earlier (FOLLOW), and to the right of any activated later this session.
      pinnedPage = page;
      swapPartner = null;   // the partner is tied to the pin — reset the SWAP cycle
      if (indicatorOrder.indexOf('pinned') === -1) indicatorOrder.push('pinned');
      renderIndicators();
      break;
    }
  }
}

// Toggle the browser's fullscreen mode on the whole page. Webkit prefix is for older Safari.
function toggleFullscreen() {
  const d = document, el = d.documentElement;
  if (!d.fullscreenElement && !d.webkitFullscreenElement) {
    (el.requestFullscreen || el.webkitRequestFullscreen || function(){}).call(el);
  } else {
    (d.exitFullscreen || d.webkitExitFullscreen || function(){}).call(d);
  }
}

// Hide/show the whole shell (frame chrome + strips + side keys). Collapsing it lets the
// screen fill the viewport; a resize event re-runs the page/pane layout for the new size.
function setShellHidden(hidden) {
  document.querySelector('.mfd').classList.toggle('shell-hidden', hidden);
  window.dispatchEvent(new Event('resize'));
}
document.getElementById('shell-restore').addEventListener('click', function() {
  setShellHidden(false);
});

// Event delegation covers both generated keys and standalone controls.
document.querySelector('.mfd').addEventListener('click', function(e) {
  const k = e.target.closest('.key');
  if (k) mfdButton(k);
});

window.addEventListener('resize', function() {
  // Orientation can flip on resize without matchMedia's 'change' always firing in every
  // environment, so refresh + re-broadcast here too (resize is guaranteed to fire).
  applyShellOrientation();
  broadcastOrientation();
  // Re-align labels to the (moved) bezel keys. In split mode the labels belong to the
  // per-pane layout, so re-run renderSplitLabels — calling showPage(currentPage) here
  // would clobber the split bezel with the single-pane page's full 6-item layout.
  if (splitMode) { renderSplitLabels(); forwardWpnLayoutToPanes(); forwardAvnLayoutToPanes(); }
  else           showPage(currentPage);
  positionSoiRing();   // the recess/panes resized — keep the ring on the focused surface
});
// MAP's R+/R- visibility depends on whether any route is saved, W+/W-'s on whether one is active
// (showPage's 'map' branch, mapSplitItems' split-pane twin) — a route created/deleted/(de)activated
// from the WPT page or a first long-press placed on MAP itself all write localStorage from a
// DIFFERENT document (the wpt/map iframe), which is exactly what fires 'storage' here (same-document
// writes don't fire their own document's listener). Re-render live to pick that up while MAP is
// showing, in full view or a split pane.
window.addEventListener('storage', function(e) {
  if (e.key !== WaypointsStore.STORE_KEY) return;
  if (splitMode) { if (panePages.indexOf('map') !== -1) renderSplitLabels(); }
  else if (currentPage === 'map') showPage('map');
});

loadConfigUrls();
showPage('main');   // start on the MAIN page
flickerScreen();    // CRT boot flicker on first load
runBootLoading();   // boot loader in the centre info box on first load
