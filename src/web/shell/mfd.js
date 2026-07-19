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
const pageFrame = document.getElementById('page-frame');   // full-view host for the frame-hosted pages (WPN, TGT, TGP)
// Pages that render in #page-frame in full view (rather than as overlay renderers). Maps the
// page name to its bare URL; showPage switches the frame's src as you move between them.
const FRAME_PAGES = { wpn: '/wpn', tgp: '/tgp', avn: '/avn', rwr: '/rwr', tgt: '/tgt', hud: '/hud', bdf: '/bdf', pal: '/bdf?enemy' };
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
// NAV item i lands on left-column key i. Uniform across every page today, so it's derived rather
// than declared — the answer to layouts.md's "is placement derivable from the ordered list?" is
// YES for full view (and no for split; see SPLIT_SLOTS). A future page needing a right-column
// full-view label is the point at which this earns a placement table of its own.
function fullViewSlot(i) { return { bank: 'left', index: i }; }

// Screens this layout puts on the glass beyond NAV's, with the key each takes. The mirror of the
// F-35's own MAIN_EXTRAS, and here for the same reason: NAV is shared and pinned at MAIN's six
// items (nav-model.test.js), one per left-bank key, so a layout that wants more names them itself.
// LYT could not go in NAV even if there were room — the F-35 already offers this choice from its
// master strip, so a NAV entry would put it on that layout's MAIN a second time.
//
// These carry their own key, which NAV items never may: fullViewSlot fills the left bank in order
// and MAIN's six fill it exactly, so LYT is the first label this shell has ever had to place
// anywhere else. That is the case the note above predicted; it is one item, so it names its key
// rather than earning a table of its own.
// LAYOUT is three left-bank labels and nothing else — the way back, then the two layouts. It draws
// no panel: every other page in this shell puts its items beside a physical key, and a chooser is
// navigation, so it reads as one. `mark` is the layout you are already on.
const BEZEL_EXTRAS = {
  // HUD, LYT, BDF and PAL down the right bank — the layout-owned MAIN keys the six shared NAV items
  // (left bank) leave no room for. HUD opens the HUD OPTIONS #page-frame page, BDF the faction-forces
  // one for the player's own faction, and PAL the same panel for the ENEMY faction (docs/bdf-page.md);
  // each gets its MAIN back from NAV like every other frame page, so none needs an entry of its own
  // here.
  main: [
    { label: 'HUD', action: 'hud', bank: 'right', index: 0 },
    { label: 'LYT', action: 'lyt', bank: 'right', index: 1 },
    { label: 'BDF', action: 'bdf', bank: 'right', index: 2 },
    { label: 'PAL', action: 'pal', bank: 'right', index: 3 },
  ],
  // No MAIN back-item under lyt here — picking CLASSIC already navigates back to MAIN (this shell),
  // so a separate way-back label would be redundant with it.
  lyt:  [
    { label: 'CLASSIC', action: 'lyt-classic', bank: 'left', index: 0, mark: true },
    { label: 'F-35',    action: 'lyt-f35',     bank: 'left', index: 1 },
  ],
};

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
const WPN_SPLIT_MAX = 4;

// Latest connection status mirrored from the map iframe — kept so we can push the
// current value to a freshly-loaded pane iframe (its onload may fire AFTER the
// shell has already received and forwarded the last status broadcast).
let lastStatusCls  = 'disconnected';
let lastStatusText = '● DISCONNECTED';

// ── Bezel layout renderer: split placement ───────────────────────────────────────────
// Where each NAV item lands in a split pane, as pane-local { side, slot } — index-aligned with
// NAV[page], so entry i places NAV[page][i]. SplitKeymap.paneKey resolves the pane-local position
// to a physical bezel key per orientation (top/bottom vs left/right).
//
// Unlike full view, split placement is NOT derivable from the ordered list: MAP deliberately
// groups its zoom rocker (Z+ over Z-) on the RIGHT column instead of filling the left first, so
// each split-capable page declares its own. (layouts.md flags this as the open question — the
// answer is "a page can need a hint", and MAP is the page that needs one.)
//
// A page absent here cannot be a split pane: HUD is full-view only (dense mode/category/type grid),
// so picking it from a pane collapses the split instead (see mfdButton's pane branch).
const SPLIT_SLOTS = {
  main: [
    { side: 'left',  slot: 0 },   // AVN
    { side: 'left',  slot: 1 },   // MAP
    { side: 'left',  slot: 2 },   // RWR
    { side: 'right', slot: 0 },   // TGT
    { side: 'right', slot: 1 },   // TGP
    { side: 'right', slot: 2 },   // WPN
  ],
  // MAP pane is the bare map iframe (/map-view?bare) — it self-connects to the SSE stream, so the
  // shell forwards no data, only routes these controls to the pane's own map. Left column = nav
  // (MAIN back) + follow; right column = the zoom rocker.
  map: [
    { side: 'left',  slot: 0 },   // MAIN — back to MAIN (this pane)
    { side: 'left',  slot: 1 },   // FLW  — toggle follow on this pane's map
    { side: 'right', slot: 0 },   // Z+
    { side: 'right', slot: 1 },   // Z-
  ],
  // AVN / TGP / RWR / TGT / BDF / PAL in a split pane each expose their single MAIN back-button on
  // the pane's top-left slot (L0 for top, physically L3 for bottom). It navigates ONLY that pane.
  // TGT's filter toggles are clickable inside the pane iframe, and BDF/PAL are read-only, so like
  // the others they need no key labels beyond MAIN.
  avn: [ { side: 'left', slot: 0 } ],
  tgp: [ { side: 'left', slot: 0 } ],
  rwr: [ { side: 'left', slot: 0 } ],
  tgt: [ { side: 'left', slot: 0 } ],
  bdf: [ { side: 'left', slot: 0 } ],
  pal: [ { side: 'left', slot: 0 } ],
  // WPN is a valid split page but places no NAV labels: its MAIN/PREV + NEXT depend on the pane's
  // pagination state, so renderSplitLabels' list branch owns them (NAV.wpn is empty to match).
  wpn: [],
};

// URL for each iframe-served page. Pages without an entry render 'about:blank' on
// navigation — a no-op signal rather than a crash.
const PAGE_URL = {
  main: '/main?bare',
  map:  '/map-view?bare',
  avn:  '/avn?bare',
  tgp:  '/tgp?bare',
  wpn:  '/wpn?bare',
  rwr:  '/rwr?bare',
  tgt:  '/tgt?bare',
  bdf:  '/bdf?bare',
  pal:  '/bdf?bare&enemy',
};
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
    // Re-forward list-page geometry so WPN panes re-lay-out for the new orientation (else they
    // keep the previous layout's row arrangement — e.g. H's 2-column grid in a V column).
    forwardWpnLayoutToPanes();
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
  // Restored on unsplit (showPage re-toggles it). TGT, BDF and PAL split into a pane like the other
  // frame pages (they get their upright pane MAIN via the per-label vlabel class instead); HUD does
  // not (not in PAGE_URL), so picking it from a pane collapses the split instead.
  overlayEl.classList.remove('vmain');
  if (splitMode) {
    paneFollowOn = [false, false];   // fresh panes; follow restarts off, re-reported on load
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
}

// Place per-pane labels for both panes' current pages. Each pane-local (side, slot) resolves to
// a physical key via paneKey, which depends on the split orientation: top/bottom (H) gives each
// pane both columns (pane 1 offset +3); left/right (V/VW) gives each pane its own column. Labels
// are tagged with data-pane so the click dispatcher knows which pane to update.
function isListPage(page) { return page === 'wpn'; }

// Physical keys for a paginated list page (WPN) in split pane paneIdx, per orientation:
//   h    → MAIN at the pane's L0, NEXT at R0, the 4 rows at L1,L2,R1,R2 (the 2×2 grid).
//   v/vw → one column: MAIN at key 0 (top), the 4 rows at keys 1..4, NEXT at key 5 (bottom).
// Returns {bank,index} for main/next/each item, plus the per-item side class the page renders with.
// The 'h' branch is identical to the pre-vertical-split behaviour, so H_SPLIT is unchanged.
function listPaneLayout(paneIdx) {
  if (splitVariant === 'h') {
    const off = paneIdx * 3;
    return {
      main: { bank: 'left', index: off }, next: { bank: 'right', index: off },
      items: [{ bank: 'left', index: off + 1 }, { bank: 'left', index: off + 2 },
              { bank: 'right', index: off + 1 }, { bank: 'right', index: off + 2 }],
      itemSides: ['left', 'left', 'right', 'right'],
    };
  }
  const side = paneIdx === 0 ? 'left' : 'right';   // left/right pane owns its adjacent column
  return {
    main: { bank: side, index: 0 }, next: { bank: side, index: 5 },
    items: [{ bank: side, index: 1 }, { bank: side, index: 2 },
            { bank: side, index: 3 }, { bank: side, index: 4 }],
    itemSides: [side, side, side, side],
  };
}

// Place an overlay label on a physical key {bank,index} and tag it with the owning pane. Returns the
// label element so the caller can style it (e.g. the vertical MAIN for a TGT pane).
function placeSplitKey(m, label, action, paneTag) {
  const el = placeOverlayLabel(m.bank, m.index, label, action);
  const k = keyBanks[m.bank] && keyBanks[m.bank][m.index];
  if (k) k.dataset.pane = paneTag;
  return el;
}

// Pages whose own content sits in the top-left where the MAIN bezel label lands, so that label is
// stood upright to clear it — in full view via .overlay.vmain, in a split pane via a per-label class
// (renderSplitLabels). TGT's RESET FILTER, HUD's mode/category rows, and BDF/PAL's WARHEADS readout
// are that content; HUD is full-view only, so only TGT and BDF/PAL actually reach the split path.
function isVmainPage(p) { return p === 'tgt' || p === 'hud' || p === 'bdf' || p === 'pal'; }

function renderSplitLabels() {
  clearKeyActions();
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  for (let paneIdx = 0; paneIdx < 2; paneIdx++) {
    const page = panePages[paneIdx];
    const slots = SPLIT_SLOTS[page];
    if (!slots) continue;                            // not a split-capable page (e.g. TGT)
    const paneTag = paneIdx === 0 ? 'top' : 'bot';   // pane identity for click dispatch (orientation-agnostic)

    if (isListPage(page)) {
      // Paginated list (WPN): MAIN (or PREV once scrolled) on the pane's top key, NEXT on its
      // bottom key — positions per orientation via listPaneLayout; the 4 rows sit on .items.
      const L = listPaneLayout(paneIdx);
      const slice = wpnPaneSlice(paneIdx);
      placeSplitKey(L.main, slice.hasPrev ? 'PREV' : 'MAIN', slice.hasPrev ? 'wpn-prev' : 'main', paneTag);
      if (slice.hasNext) placeSplitKey(L.next, 'NEXT', 'wpn-next', paneTag);
      // Wire this pane's weapon rows to weapon.select. No data-pane tag — weapon selection is
      // aircraft-global, so the press falls through the pane dispatcher to the shared case.
      wireWpnPaneWeaponKeys(slice.items, paneIdx);
    } else {
      // Static nav (MAIN/MAP/AVN/RWR/TGP): render the navigation model at this page's declared
      // pane-local slots — SPLIT_SLOTS[page][i] places NAV[page][i].
      (NAV[page] || []).forEach(function(item, i) {
        const s = slots[i];
        // SPLIT_SLOTS is index-aligned with NAV, so a NAV item added without a matching slot would
        // silently not render here — the exact failure the old duplicated tables produced. Say so.
        if (!s) { console.warn('[mfd] NAV.' + page + '[' + i + '] "' + item.label + '" has no SPLIT_SLOTS entry — not placed'); return; }
        const el = placeSplitKey(paneKey(paneIdx, s.side, s.slot), item.label, item.action, paneTag);
        // TGT keeps clickable content (RESET FILTER) under its MAIN label; stand it upright in the
        // pane too, the way full view does via .overlay.vmain. Only the MAIN back-item of a vmain page.
        if (el && isVmainPage(page) && item.action === 'main') el.classList.add('vlabel');
      });
    }
  }
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
    }, '*');
  });
}
// Full-view AVN: forward the snapshot to the #page-frame iframe (same payload as the panes).
function forwardAvnToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'avn', name: avnData.name, parts: avnData.parts,
                  failures: avnData.failures, fuel: avnData.fuel, throttle: avnData.throttle,
                  hasAb: avnData.hasAb, abStart: avnData.abStart,
                  gearDown: avnData.gearDown, radar: avnData.radar, guns: avnData.guns,
                  ignition: avnData.ignition, assist: avnData.assist, turret: avnData.turret,
                  nvg: avnData.nvg, navLights: avnData.navLights }, '*');
}
// Forward the full-view geometry: AVN's header band (name + status) fills the top bezel row —
// from below the first separator sep[0] to above the second sep[1] — and the silhouette frame
// spans from below sep[1] to the bottom strip (last sep). Map the shell-viewport coords into the
// frame by subtracting its top. The page's full profile applies this (compact uses CSS offsets).
function forwardAvnLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  const geom = {};
  if (sepEls.length >= 2) {
    const sep0 = sepEls[0].getBoundingClientRect();   // top separator (above key[0])
    const sep1 = sepEls[1].getBoundingClientRect();   // below key[0] — bottom of the top bezel row
    const botSep = sepEls[sepEls.length - 1].getBoundingClientRect();
    geom.headerTop    = sep0.bottom - frameTop;       // name + status band …
    geom.headerHeight = sep1.top - sep0.bottom;       // … the top bezel row
    geom.frameTop     = sep1.bottom - frameTop;       // silhouette + bars start below sep[1]
    geom.frameHeight  = botSep.top - sep1.bottom;
  }
  w.postMessage({ mfd: true, type: 'avn-layout', layout: 'full', geom: geom }, '*');
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
// Full-view PAL: same as forwardBdfToFrame, for the enemy-faction block (docs/bdf-page.md).
function forwardPalToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage(Object.assign({ mfd: true, type: 'pal' }, palData), '*');
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
// Slice the full loadout to the page a given pane is scrolled to. Returns the visible rows
// plus whether PREV/NEXT exist, so renderSplitLabels can place the right nav labels. Clamps
// a stale page index (e.g. the loadout shrank) back into range as a side effect.
function wpnPaneSlice(idx) {
  const list = wpnData.items || [];
  const total = list.length;
  const maxPage = Math.max(0, Math.ceil(total / WPN_SPLIT_MAX) - 1);
  if (paneWpnPage[idx] > maxPage) paneWpnPage[idx] = maxPage;
  if (paneWpnPage[idx] < 0)       paneWpnPage[idx] = 0;
  const start = paneWpnPage[idx] * WPN_SPLIT_MAX;
  const items = list.slice(start, start + WPN_SPLIT_MAX);
  return { items: items, hasPrev: paneWpnPage[idx] > 0, hasNext: start + items.length < total,
           page: maxPage > 0 ? paneWpnPage[idx] + 1 : 1, pages: maxPage + 1 };
}
function forwardWpnToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'wpn') return;
    if (!iframe.contentWindow) return;
    const sl = wpnPaneSlice(idx);
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'wpn', items: sl.items, selWeapon: wpnData.selWeapon,
        page: sl.page, pages: sl.pages }, '*');
  });
}
// 0-indexed page that holds the currently selected weapon, or -1 if there's no selection
// (or it isn't in the loadout).
function selWeaponPage() {
  const list = wpnData.items || [];
  const sel  = wpnData.selWeapon;
  if (!sel) return -1;
  const i = list.findIndex(function(w) { return w.n === sel; });
  return i < 0 ? -1 : Math.floor(i / WPN_SPLIT_MAX);
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
    const L = listPaneLayout(idx);
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
// aligned physical key's action + weapon name so a bezel press selects that weapon. No data-pane
// tag: selection is aircraft-global, so the press falls through to the shared weapon.select case.
// Called from renderSplitLabels after clearKeyActions has cleared the key zone, so only occupied
// rows are set and empty ones stay clean.
function wireWpnPaneWeaponKeys(weapons, paneIdx) {
  const items = listPaneLayout(paneIdx).items;
  for (let i = 0; i < items.length && i < weapons.length; i++) {
    const key = keyBanks[items[i].bank][items[i].index];
    if (key) { key.dataset.action = 'weapon.select'; key.dataset.wname = weapons[i].n; }
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
// on page 0 / PREV after; right key-0 is NEXT when the loadout overflows the page. WPN has no
// other overlay labels, so we can safely clear all overlay-items before re-placing.
function placeWpnNavLabels() {
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  delete keyBanks.left[0].dataset.action;
  delete keyBanks.right[0].dataset.action;
  const total = (wpnData.items || []).length;
  const maxPage = Math.max(0, Math.ceil(total / WPN_MAX_DISPLAY) - 1);
  const cur = Math.min(Math.max(wpnPage, 0), maxPage);
  placeOverlayLabel('left', 0, cur > 0 ? 'PREV' : 'MAIN', cur > 0 ? 'wpn-prev' : 'main');
  if (cur < maxPage) placeOverlayLabel('right', 0, 'NEXT', 'wpn-next');
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
    else if (page === 'avn')  forwardAvnToPanes();
    else if (page === 'tgp')  forwardTgpToPanes();
    else if (page === 'rwr')  { forwardRwrToPanes(); forwardMwToPanes(); }
    else if (page === 'tgt')  { forwardTgtToPanes(); forwardTgtTargetsToPanes(); }
    else if (page === 'bdf')  forwardBdfToPanes();
    else if (page === 'pal')  forwardPalToPanes();
    else if (page === 'wpn')  { forwardWpnToPanes(); forwardCmToPanes(); forwardWpnLayoutToPanes(); }
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
  else if (currentPage === 'rwr') { forwardRwrToFrame(); forwardMwToFrame(); }
  else if (currentPage === 'tgt') { forwardTgtToFrame(); forwardTgtTargetsToFrame(); }
  else if (currentPage === 'bdf') { forwardBdfToFrame(); }
  else if (currentPage === 'pal') { forwardPalToFrame(); }
});

// Top-right indicator stack (PINNED + FOLLOW). pinnedPage tracks which page (if any)
// is currently pinned; followOn mirrors the map iframe's follow state (broadcast via
// postMessage). indicatorOrder records the chronological order indicators were turned
// on — the first activated stays at the right edge and later arrivals stack to its
// left, matching how chips render with flex-direction:row-reverse on #mfd-indicators.
const indicatorsEl = document.getElementById('mfd-indicators');
let pinnedPage    = null;
let followOn      = false;
// Per-pane map follow state for split mode — each MAP pane's iframe broadcasts its own
// follow. The FOLLOW chip shows whenever a currently-visible MAP pane is following.
let paneFollowOn  = [false, false];
let indicatorOrder = [];   // subset of ['pinned','follow'] in activation order
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
  // mode, the single stack in full view.
  if (name === 'pinned') {
    return pinnedPage !== null &&
      (splitMode ? panePages[topRightPane()] === pinnedPage : currentPage === pinnedPage);
  }
  // Shell-stack FOLLOW is single-mode only (one map fills the screen). Split mode renders
  // a FOLLOW chip per pane instead — see renderPaneFollow().
  if (name === 'follow') return !splitMode && currentPage === 'map' && followOn;
  return false;
}
// Paint a FOLLOW chip in the top-right of each pane that's showing a following MAP. Split
// mode only; in single mode both per-pane boxes are cleared (the shell stack handles it).
function renderPaneFollow() {
  [0, 1].forEach(function(i) {
    const box = document.getElementById(i === 0 ? 'follow-top' : 'follow-bot');
    const on  = splitMode && panePages[i] === 'map' && paneFollowOn[i];
    box.innerHTML = on ? '<div class="mfd-indicator">FOLLOW</div>' : '';
    box.classList.toggle('show', on);
  });
}
// Recompute both FOLLOW surfaces: the single-mode shell-stack chip and the split-mode
// per-pane chips. Called whenever follow state, pane pages, or split mode change.
function refreshFollowIndicator() {
  const single = !splitMode && currentPage === 'map' && followOn;
  const has = indicatorOrder.indexOf('follow') !== -1;
  if (single && !has) indicatorOrder.push('follow');
  else if (!single && has) indicatorOrder = indicatorOrder.filter(function(x) { return x !== 'follow'; });
  renderIndicators();
  renderPaneFollow();
}

function renderIndicators() {
  indicatorsEl.innerHTML = '';
  indicatorOrder.forEach(function(name) {
    if (!indicatorVisible(name)) return;
    const el = document.createElement('div');
    el.className = 'mfd-indicator';
    el.textContent = name === 'pinned' ? 'PINNED' : 'FOLLOW';
    indicatorsEl.appendChild(el);
  });
}

// Latest loadout snapshot mirrored from the map iframe (postMessage). Even when WPN isn't
// in view we keep it fresh, so opening the page renders immediately without a round-trip.
let wpnData      = { items: [], selWeapon: null };
let wpnPage = 0;             // 0-indexed page for the weapon list pagination (full-view nav state)
const WPN_MAX_DISPLAY = 5;   // weapons per page = 5 line-select slots (keys 1..5)

// 0-indexed full-view page (WPN_MAX_DISPLAY per page) holding the selected weapon, or -1 if
// there's no selection (or it isn't in the loadout). Full-view twin of selWeaponPage().
function selWpnPageFull() {
  const list = wpnData.items || [];
  const sel  = wpnData.selWeapon;
  if (!sel) return -1;
  const i = list.findIndex(function(w) { return w.n === sel; });
  return i < 0 ? -1 : Math.floor(i / WPN_MAX_DISPLAY);
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
let avnData = { name: null, parts: null, failures: null, fuel: -1, throttle: -1, hasAb: false, abStart: 1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false };

// Latest RWR emitters + incoming missiles, mirrored from the map iframe's SSE feed. The shell
// keeps only this state (the forwarders read it); all scope SVG rendering lives in src/web/pages/rwr/.
let rwrData = { items: [] };
let mwData  = { items: [] };

// Latest TGT filter state, mirrored from the map iframe's SSE feed. The shell keeps only this
// state and forwards it to the frame; the page renders the toggles + POSTs the tgt.* commands.
let tgtData = { present: false };

// Latest BDF faction-forces state, mirrored from the map iframe's SSE feed (docs/bdf-page.md).
// The shell keeps only this state and forwards it to the frame or the pane showing it.
let bdfData = { present: false };
// Same, for the ENEMY faction's PAL panel (docs/bdf-page.md).
let palData = { present: false };

function clearKeyActions() {
  // Only the page-dynamic banks (left/right) get cleared between pages. The top and bottom
  // banks hold page-independent controls (fullscreen on top; PIN, SWAP, layout… on bottom)
  // whose actions are wired once at startup and must survive page switches.
  ['left', 'right'].forEach(function(bank) {
    keyBanks[bank].forEach(function(k) {
      delete k.dataset.action;
      delete k.dataset.pane;     // split-mode tag; harmless to clear unconditionally
      delete k.dataset.wname;    // weapon.select name (WPN page); clear so it never lingers
    });
  });
}

// `mark` lights the label in the engaged amber — only LAYOUT's current item uses it; every other
// label names a page rather than a state.
function placeOverlayLabel(bankName, keyIndex, label, action, mark) {
  const side = bankName || 'left';
  const bank = keyBanks[side];
  const k = bank && bank[keyIndex];
  if (!k) return null;

  if (action) k.dataset.action = action;
  const el = document.createElement('div');
  el.className = 'overlay-item ' + side + (mark ? ' on' : '');
  el.textContent = label;

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
  // Only wipe dynamic line-select labels; static children (info-box) stay put.
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });

  // Bezel full-view rendering of the navigation model: item i → left-column key i.
  (NAV[name] || []).forEach(function(item, i) {
    const m = fullViewSlot(i);
    placeOverlayLabel(m.bank, m.index, item.label, item.action);
  });
  // ...then this layout's own, which name their own key (see BEZEL_EXTRAS). Only full view runs
  // through here, so LYT never appears on a split pane's MAIN — which is what "full-view only"
  // means, with nothing to enforce it.
  (BEZEL_EXTRAS[name] || []).forEach(function(item) {
    placeOverlayLabel(item.bank, item.index, item.label, item.action, item.mark);
  });

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
  // AVN renders in #page-frame too. Its only key is the static MAIN label (NAV.avn,
  // placed by the generic sweep above); forward the bezel geometry (full profile) + snapshot.
  if (name === 'avn') {
    showFramePage('avn');
    forwardAvnLayoutToFrame(); forwardAvnToFrame();
  }
  // RWR renders in #page-frame too. Its only key is the static MAIN label (NAV.rwr,
  // placed by the generic sweep above); forward the contact + missile snapshots.
  if (name === 'rwr') {
    showFramePage('rwr');
    forwardRwrToFrame(); forwardMwToFrame();
  }
  // TGT renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.tgt,
  // placed by the generic sweep above); everything else is clickable in the page. Forward state.
  if (name === 'tgt') {
    showFramePage('tgt');
    forwardTgtToFrame();
    forwardTgtTargetsToFrame();
  }
  // BDF renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.bdf, placed
  // by the generic sweep above) — the right-bank BDF key itself lives in BEZEL_EXTRAS, not NAV, so
  // it's reached in full view and carried into a split by splitting from there (SPLIT_SLOTS.bdf).
  // Forward the faction-forces state.
  if (name === 'bdf') {
    showFramePage('bdf');
    forwardBdfToFrame();
  }
  // PAL renders in #page-frame too — same as BDF, for the enemy-faction block.
  if (name === 'pal') {
    showFramePage('pal');
    forwardPalToFrame();
  }
  // HUD renders in #page-frame too. Its only bezel key is the static MAIN label (NAV.hud, placed by
  // the generic sweep above); the page is otherwise self-driven — it fetches /hud-options and POSTs
  // its own hud.* commands, so the shell forwards it nothing.
  if (name === 'hud') showFramePage('hud');

  // refreshFollowIndicator (not just renderIndicators) because the FOLLOW chip's membership
  // depends on currentPage, which just changed: entering MAP with follow already on must add the
  // chip now (the map's follow state was reported earlier, while another page was in view), and
  // leaving MAP must drop it. It renders the full indicator stack (incl. PINNED) internally.
  refreshFollowIndicator();
}

// The map iframe broadcasts status + loadout + cm via postMessage; mirror onto the
// info-box (MAIN page), the cached wpnData + cmData (WPN page).
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  // Telemetry-mirror messages come only from the canonical map iframe (mapFrame). In split mode
  // a MAP *pane* is a second map iframe that also streams to the shell; ignoring its duplicate
  // data posts keeps the RWR/AVN/etc. mirrors on a single source — otherwise two out-of-phase
  // feeds drive them (jumpy in preview, redundant live). 'follow' is per-pane and routes by
  // e.source itself, so it must pass through from any map source.
  if (m.type !== 'follow' && e.source !== mapFrame.contentWindow) return;
  if (m.type === 'status') {
    lastStatusCls  = m.cls;
    lastStatusText = m.text;
    ibStatus.className = 'ib-status mfd-status ' + m.cls;
    ibStatus.textContent = m.text;
    if (splitMode) forwardStatusToPanes();
  } else if (m.type === 'loadout') {
    const prevSel = wpnData.selWeapon;
    wpnData = { items: m.items || [], selWeapon: m.selWeapon || null };
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
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
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
    if (splitMode) forwardAvnToPanes();
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
    // Same as 'bdf', for the enemy-faction block.
    palData = m;
    if (currentPage === 'pal' && !splitMode) forwardPalToFrame();
    if (splitMode) forwardPalToPanes();
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
    } else if (act === 'flw' || act === 'zin' || act === 'zout') {
      // MAP controls act on the pane's own map iframe — they don't navigate it away.
      paneMapSend(paneIdx, act === 'flw' ? 'toggle-follow' : act === 'zin' ? 'zoom-in' : 'zoom-out');
    } else {
      paneNavigate(paneIdx, act);
    }
    return;
  }

  switch (el.dataset.action) {
    case 'main': showPage('main'); mapSend('status-request'); break;   // pull fresh status on open
    case 'map':  showPage('map');  break;
    case 'wpn':       wpnPage = Math.max(0, selWpnPageFull()); showPage('wpn'); break;   // open on the selected weapon's page
    case 'wpn-prev':  wpnPage--;   showPage('wpn'); break;   // renderWpn clamps on overshoot
    case 'wpn-next':  wpnPage++;   showPage('wpn'); break;
    case 'weapon.select':                                    // WPN bezel key → select the aligned weapon
      if (el.dataset.wname) sendCommand('weapon.select', { wname: el.dataset.wname }).catch(function() {});
      break;
    case 'tgp':  showPage('tgp');  break;
    case 'hud':  showPage('hud');  break;
    case 'lyt':  showPage('lyt');  break;
    // The LAYOUT page's two choices. CLASSIC is this document, so choosing it is just leaving the
    // menu — back to MAIN, where LYT was pressed, with a fresh status as MAIN's own key pulls.
    // F-35 is a different document, so it is a real navigation; that shell lands on its own MAIN.
    // Either choice is remembered (setLayout → localStorage) so a fresh load honors it — the head
    // guard in each shell's HTML redirects on that value (docs/layouts.md, Stage 3).
    case 'lyt-classic': setLayout('classic'); showPage('main'); mapSend('status-request'); break;
    case 'lyt-f35':     setLayout('f35'); location.href = '/f35'; break;
    case 'avn':  showPage('avn');  break;
    case 'rwr':  showPage('rwr');  break;
    case 'tgt':  showPage('tgt');  break;
    case 'bdf':  showPage('bdf');  break;
    case 'pal':  showPage('pal');  break;
    case 'flw':  mapSend('toggle-follow'); break;
    case 'zin':  mapSend('zoom-in');  break;
    case 'zout': mapSend('zoom-out'); break;
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
  if (splitMode) { renderSplitLabels(); forwardWpnLayoutToPanes(); }
  else           showPage(currentPage);
});
loadConfigUrls();
showPage('main');   // start on the MAIN page
flickerScreen();    // CRT boot flicker on first load
runBootLoading();   // boot loader in the centre info box on first load
