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
// so they survive page switches. Entries without an `action` render the icon but do nothing
// yet — placeholders to be wired later (left/right splits, power).
const layoutIcons = [
  { cls: 'ic-square', title: 'Full view',            action: 'unsplit' },
  { cls: 'ic-2x1',    title: 'Split top/bottom',     action: 'split' },
  { cls: 'ic-1x2',    title: 'Split left/right' },
  { cls: 'ic-lr23',   title: 'Split left/right 2/3' },
];
const functionIcons = [
  { cls: 'ic-power',      title: 'Power',      action: 'power' },
  { cls: 'ic-fullscreen', title: 'Fullscreen', action: 'fll' },
  { cls: 'ic-swap',       title: 'Swap',       action: 'swap' },
  { cls: 'ic-pin',        title: 'Pin',        action: 'pin' },
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
const overlayEl = document.getElementById('overlay');
const mapFrame  = document.querySelector('.screen > iframe[title="map"]');
const screenEl  = document.getElementById('screen');
const paneIframes = [document.getElementById('pane-top'), document.getElementById('pane-bot')];
const pageFrame = document.getElementById('page-frame');   // full-view host for the frame-hosted pages (WPN, TGL, TGP)
// Pages that render in #page-frame in full view (rather than as overlay renderers). Maps the
// page name to its bare URL; showPage switches the frame's src as you move between them.
const FRAME_PAGES = { wpn: '/wpn', tgl: '/tgl', tgp: '/tgp', avn: '/avn', rwr: '/rwr' };
const infoBox   = document.getElementById('info-box');
const ibStatus  = document.getElementById('ib-status');
// (TGP's panel/img + has-feed handling live in src/web/pages/tgp/, hosted in #page-frame.)
const sepEls      = document.querySelectorAll('#keys-left .sep');   // 0 = above key[0], i+1 = below key[i]
// (No RWR element refs here — full-view RWR is hosted in #page-frame, src/web/pages/rwr/, which
//  owns the scope SVG. The shell keeps only rwrData + mwData + the forwarders below.)
// (No AVN element refs here — full-view AVN is hosted in #page-frame, src/web/pages/avn/, which
//  owns the silhouette/bars DOM. The shell keeps only avnData + the forwarders below.)
// (No WPN/CM overlay element refs here — full-view WPN is hosted in #page-frame, which owns
//  its own weapon rows + CM panel; see src/web/pages/wpn/.)

// ── Pages ─────────────────────────────────────────────────────────────────────────
// Which page is in view (MAP, MAIN, WPN…) and the line-select items each page shows.
// Every item names a label, the key bank/slot it aligns to, and the action its key
// fires. The MAP page overlays its items on top of the (still-interactive) map; the
// MAIN page draws an opaque panel over it.
const PAGES = {
  map: {
    opaque: false,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },   // → MAIN page
      { label: 'FLW',  key: 1, action: 'flw'  },   // → toggle map follow
      { label: 'Z+',   key: 2, action: 'zin'  },   // → map zoom in
      { label: 'Z-',   key: 3, action: 'zout' },   // → map zoom out
    ],
  },
  main: {
    opaque: true,
    items: [
      { label: 'AVN', key: 0, action: 'avn' },      // → AVN page
      { label: 'MAP', key: 1, action: 'map' },      // → MAP page
      { label: 'RWR', key: 2, action: 'rwr' },      // → RWR page
      { label: 'TGL', key: 3, action: 'tgl' },      // → TGL page (target list)
      { label: 'TGP', key: 4, action: 'tgp' },      // → TGP page
      { label: 'WPN', key: 5, action: 'wpn' },      // → WPN page
    ],
  },
  wpn: {
    // Hosted in #page-frame (an iframe), not the overlay — so the overlay stays transparent
    // and only carries the nav labels. placeWpnNavLabels() owns left-key-0 (MAIN/PREV) and
    // right-key-0 (NEXT when more than WPN_MAX_DISPLAY weapons exist).
    opaque: false,
    items: [],
  },
  tgp: {
    // Hosted in #page-frame (the src/web/pages/tgp page), not the overlay — so the overlay
    // stays transparent and only carries the MAIN nav label below.
    opaque: false,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },    // ← back to MAIN
    ],
  },
  tgl: {
    // Hosted in #page-frame (the src/web/pages/tgl page), not the overlay — so the overlay
    // stays transparent and only carries the nav labels + the deselect softkeys the page emits.
    // placeTglNavLabels() owns left-key-0 (MAIN/PREV) and right-key-0 (NEXT when overflow).
    opaque: false,
    items: [],
  },
  avn: {
    // Hosted in #page-frame (the src/web/pages/avn page), not the overlay — so the overlay
    // stays transparent and only carries the MAIN nav label below.
    opaque: false,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },     // ← back to MAIN
    ],
  },
  rwr: {
    // Hosted in #page-frame (the src/web/pages/rwr page), not the overlay — so the overlay
    // stays transparent and only carries the MAIN nav label below.
    opaque: false,
    items: [
      { label: 'MAIN', key: 0, action: 'main' },     // ← back to MAIN
    ],
  },
};
let currentPage = 'map';

// ── Split-screen state ──────────────────────────────────────────────────────────────
// When splitMode is on, the screen renders two stacked iframes (the panes) instead
// of the single map iframe + overlay panels. Each pane has its own currentPage;
// the shell still owns the bezel labels and dispatches clicks to the right pane.
// See docs/mfd-split-screen.md — Strategy A, implementation sequence steps 1-4.
let splitMode = false;
// [topPage, botPage]. Step 3 of the implementation sequence seeds both panes with
// MAIN on entry; per-pane navigation updates this from MAIN's L0..L2 / R0..R2 keys.
let panePages = ['main', 'main'];
// Per-pane softkeys last emitted by each pane's page (the declarative contract — currently only
// TGL emits any). Cached so renderSplitLabels can re-apply them: it clears ALL keys, and a pane
// that didn't re-render won't re-emit. Reset when a pane navigates / on split entry.
let paneSoftkeys = [[], []];
// Per-pane WPN pagination index. WPN's weapon list can exceed one split page; each pane
// scrolls independently via its PREV/NEXT bezel labels. Reset to 0 when a pane (re)enters
// WPN. The bare WPN page is a pure renderer — the shell slices the list here.
//
// A split pane shows at most 4 weapons (slots L1, L2, R1, R2). The top band's keys are
// reserved: L0 = MAIN/PREV back-button, R0 = NEXT (shown only when the loadout exceeds 4).
let paneWpnPage = [0, 0];
const WPN_SPLIT_MAX = 4;

// Per-pane TGL pagination index. The target list can exceed one split page (4 slots: L1, L2,
// R1, R2); each pane scrolls independently via PREV/NEXT, with MAIN/PREV on L0 and NEXT on R0.
// The bare TGL page is a pure renderer — the shell slices the list here. Reset to 0 on entry.
let paneTglPage = [0, 0];
const TGL_SPLIT_MAX = 4;

// Latest connection status mirrored from the map iframe — kept so we can push the
// current value to a freshly-loaded pane iframe (its onload may fire AFTER the
// shell has already received and forwarded the last status broadcast).
let lastStatusCls  = 'disconnected';
let lastStatusText = '● DISCONNECTED';

// Split-mode line-select layouts per page. Each entry is one pane-local label;
// physical key index = slot + paneOffset (paneOffset = 0 for top, 3 for bottom).
// Only pages we've remapped via the interview in docs/mfd-split-screen.md appear
// here. Pages without an entry render no labels in split mode (yet).
const SPLIT_PAGES = {
  main: {
    // Initial mapping scope per the user — only AVN and TGP are wired today. Other
    // destinations (MAP/RWR/TGL/WPN) come in subsequent interview rounds and stay
    // hidden until their bare pages exist.
    items: [
      { side: 'left',  slot: 0, label: 'AVN', action: 'avn' },
      { side: 'left',  slot: 1, label: 'MAP', action: 'map' },
      { side: 'left',  slot: 2, label: 'RWR', action: 'rwr' },
      { side: 'right', slot: 0, label: 'TGL', action: 'tgl' },
      { side: 'right', slot: 1, label: 'TGP', action: 'tgp' },
      { side: 'right', slot: 2, label: 'WPN', action: 'wpn' },
    ],
  },
  // MAP pane is the bare map iframe (/map-view?bare) — it self-connects to the SSE stream,
  // so the shell forwards no data, only routes these controls to the pane's own map. Left
  // column = nav (MAIN back) + follow; right column = zoom rocker (Z+ over Z-).
  map: {
    items: [
      { side: 'left',  slot: 0, label: 'MAIN', action: 'main' },   // ← back to MAIN (this pane)
      { side: 'left',  slot: 1, label: 'FLW',  action: 'flw'  },   // toggle follow on this pane's map
      { side: 'right', slot: 0, label: 'Z+',   action: 'zin'  },   // zoom this pane's map in
      { side: 'right', slot: 1, label: 'Z-',   action: 'zout' },   // zoom this pane's map out
    ],
  },
  // AVN / TGP in a split pane each expose a single MAIN back-button on their pane's
  // top-left slot (L0 for top, physically L3 for bottom). Clicking it navigates ONLY
  // that pane back to MAIN, leaving the other pane untouched.
  avn: {
    items: [
      { side: 'left', slot: 0, label: 'MAIN', action: 'main' },
    ],
  },
  tgp: {
    items: [
      { side: 'left', slot: 0, label: 'MAIN', action: 'main' },
    ],
  },
  // RWR pane is the bare /rwr iframe — a self-contained stub (fake contacts, no fetches), so
  // the shell forwards no data. Just a MAIN back-button on the pane's top-left slot.
  rwr: {
    items: [
      { side: 'left', slot: 0, label: 'MAIN', action: 'main' },
    ],
  },
  // WPN's pane labels are dynamic — MAIN/PREV on the pane's L0 and NEXT on R0 depend on the
  // pane's pagination state — so renderSplitLabels special-cases it instead of reading a
  // static item list here. This marker just records that WPN is a valid split page.
  wpn: { dynamic: true },
  // TGL is likewise pagination-driven (MAIN/PREV on L0, NEXT on R0); renderSplitLabels
  // special-cases it. Marker records it as a valid split page.
  tgl: { dynamic: true },
};

// URL for each iframe-served page. Pages without an entry render 'about:blank' on
// navigation — a no-op signal rather than a crash.
const PAGE_URL = {
  main: '/main?bare',
  map:  '/map-view?bare',
  avn:  '/avn?bare',
  tgp:  '/tgp?bare',
  wpn:  '/wpn?bare',
  tgl:  '/tgl?bare',
  rwr:  '/rwr?bare',
};
function paneUrl(page) { return PAGE_URL[page] || 'about:blank'; }

function applySplitMode() {
  screenEl.classList.toggle('split', splitMode);
  paneSoftkeys = [[], []];          // fresh panes re-emit their softkeys on load
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

// Place per-pane labels for both panes' current pages. The top pane occupies
// physical keys L0..L2 / R0..R2 (paneOffset = 0); the bottom pane occupies
// L3..L5 / R3..R5 (paneOffset = 3). Labels are tagged with data-pane so the
// click dispatcher knows which pane to update.
function renderSplitLabels() {
  clearKeyActions();
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  for (let paneIdx = 0; paneIdx < 2; paneIdx++) {
    const page = panePages[paneIdx];
    const def = SPLIT_PAGES[page];
    if (!def) continue;
    const paneOffset = paneIdx === 0 ? 0 : 3;
    const paneTag = paneIdx === 0 ? 'top' : 'bot';
    // WPN's labels are pagination-dependent: MAIN (or PREV once scrolled) on the pane's L0,
    // NEXT on the pane's R0 when more weapons remain. Mirrors single-pane renderWpn.
    let items;
    if (page === 'wpn') {
      const sl = wpnPaneSlice(paneIdx);
      items = [{ side: 'left', slot: 0,
                 label:  sl.hasPrev ? 'PREV' : 'MAIN',
                 action: sl.hasPrev ? 'wpn-prev' : 'main' }];
      if (sl.hasNext) items.push({ side: 'right', slot: 0, label: 'NEXT', action: 'wpn-next' });
    } else if (page === 'tgl') {
      const sl = tglPaneSlice(paneIdx);
      items = [{ side: 'left', slot: 0,
                 label:  sl.hasPrev ? 'PREV' : 'MAIN',
                 action: sl.hasPrev ? 'tgl-prev' : 'main' }];
      if (sl.hasNext) items.push({ side: 'right', slot: 0, label: 'NEXT', action: 'tgl-next' });
      // The per-target deselect keys are NOT bound here — the TGL page emits them via the softkey
      // contract; they're re-applied from paneSoftkeys[paneIdx] below.
    } else {
      items = def.items;
    }
    // Apply this pane's contract softkeys FIRST: applySoftkeys clears the row-key zone (slots
    // 1..2 both sides) before re-applying any softkeys the page emitted (only TGL does). Static
    // nav items are placed AFTER, so a page like MAIN — whose nav fills slots 1..2 (MAP/RWR on
    // the left, TGP/WPN on the right), not just slot 0 — isn't clobbered by that zone-clear.
    // Softkeys (TGL deselect, slots 1..2) and the slot-0 nav are disjoint, so order is safe.
    applySoftkeys(paneSoftkeys[paneIdx], paneOffset, 2);
    items.forEach(function(item) {
      placeOverlayLabel(item.side, item.slot + paneOffset, item.label, item.action);
      const physicalKey = keyBanks[item.side][item.slot + paneOffset];
      if (physicalKey) physicalKey.dataset.pane = paneTag;
    });
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
  paneSoftkeys[paneIdx] = [];   // drop the old page's softkeys; the new page re-emits on load if any
  if (page === 'wpn') paneWpnPage[paneIdx] = Math.max(0, selWeaponPage());   // open on the selected weapon's page
  if (page === 'tgl') paneTglPage[paneIdx] = 0;                              // fresh entry — first page
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
    }, '*');
  });
}
// Full-view AVN: forward the snapshot to the #page-frame iframe (same payload as the panes).
function forwardAvnToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'avn', name: avnData.name, parts: avnData.parts,
                  failures: avnData.failures, fuel: avnData.fuel, throttle: avnData.throttle }, '*');
}
// Forward the full-view geometry: AVN's name aligns to the vertical centre of left key[0], and
// the silhouette frame spans from below the name (sep[1]) to the bottom strip (last sep). Map
// the shell-viewport coords into the frame by subtracting its top. The page's full profile
// applies this placement (compact uses CSS fixed offsets).
function forwardAvnLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const k = leftKeys[0]; if (!k) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  const kr = k.getBoundingClientRect();
  const geom = { nameTop: kr.top + kr.height / 2 - frameTop };
  if (sepEls.length >= 2) {
    const topSep = sepEls[1].getBoundingClientRect();
    const botSep = sepEls[sepEls.length - 1].getBoundingClientRect();
    geom.frameTop    = topSep.bottom - frameTop;
    geom.frameHeight = botSep.top - topSep.bottom;
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
function forwardMwToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'rwr') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'mw', items: mwData.items || [] }, '*');
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
    const off = idx === 0 ? 0 : 3;
    function cy(key) { const r = key.getBoundingClientRect(); return r.top + r.height / 2 - paneTop; }
    // Weapon slots skip the top band (R0 is reserved for NEXT): L1, L2 then R1, R2.
    const slotYs = [
      cy(keyBanks.left[off + 1]),   // L1
      cy(keyBanks.left[off + 2]),   // L2
      cy(keyBanks.right[off + 1]),  // R1
      cy(keyBanks.right[off + 2]),  // R2
    ];
    // CM band = the first key slot (between the separators flanking L0 / MAIN), so the CM
    // panel can fill that exact height — the same band single-pane WPN parks the CM panel in.
    const bandTop = sepEls[off].getBoundingClientRect().bottom - paneTop;
    const bandBot = sepEls[off + 1].getBoundingClientRect().top - paneTop;
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'wpn-layout', slotYs: slotYs, cmTop: bandTop, cmHeight: bandBot - bandTop }, '*');
  });
}

// ── Full-view WPN frame (single-pane) ──────────────────────────────────────────────────
// Full-view WPN is hosted in #page-frame (the src/web/pages/wpn page in its 'full' profile).
// These mirror the split forwarders but compute the full-screen geometry (5 left-column slots
// + the right-half image area + the CM band) from the bezel separators, and slice the loadout
// to the full-view page (WPN_MAX_DISPLAY, wpnPage).
function frameWin() { return pageFrame && pageFrame.contentWindow; }
// Point #page-frame at a frame-hosted page, switching its src when moving between frame pages
// (WPN ↔ TGL) and lazy-loading on first entry. No-op if it already shows that page.
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

// ── Full-view TGL (#page-frame) ──────────────────────────────────────────────────────
// Full-view TGL is hosted in #page-frame (src/web/pages/tgl in its 'full' profile), mirroring WPN.
// The shell slices the target list to TGL_MAX_DISPLAY (10/page, tglPage) and forwards it; the
// page renders the rows AND posts its deselect softkeys up (handled by applySoftkeys). Nav
// (MAIN/PREV/NEXT) stays shell-owned via placeTglNavLabels.
function forwardTglToFrame() {
  const w = frameWin(); if (!w) return;
  const list = tglData.targets || [];
  const total = list.length;
  const maxPage = Math.max(0, Math.ceil(total / TGL_MAX_DISPLAY) - 1);
  if (tglPage > maxPage) tglPage = maxPage;
  if (tglPage < 0) tglPage = 0;
  const start = tglPage * TGL_MAX_DISPLAY;
  const items = list.slice(start, start + TGL_MAX_DISPLAY);
  w.postMessage({ mfd: true, type: 'tgl', items: items,
                  page: maxPage > 0 ? tglPage + 1 : 1, pages: maxPage + 1 }, '*');
}
// Full-view row geometry: the 5 vertical key bands (keys 1..5), shared by both columns. Same
// sepEls model as forwardWpnLayoutToFrame: key s spans sep[s].bottom → sep[s+1].top.
function forwardTglLayoutToFrame() {
  const w = frameWin(); if (!w) return;
  const frameTop = pageFrame.getBoundingClientRect().top;
  function bot(i) { return sepEls[i].getBoundingClientRect().bottom - frameTop; }
  function top(i) { return sepEls[i].getBoundingClientRect().top - frameTop; }
  const slots = [];
  for (let s = 1; s <= 5; s++) {                 // row keys 1..5
    const t = bot(s), b = top(s + 1);
    slots.push({ top: t, height: Math.max(0, b - t) });
  }
  w.postMessage({ mfd: true, type: 'tgl-layout', layout: 'full', slots: slots }, '*');
}
// Full-view TGL nav labels (shell-owned, since pagination is shell state): left key-0 is MAIN on
// page 0 / PREV after; right key-0 is NEXT when the list overflows. Mirrors placeWpnNavLabels.
function placeTglNavLabels() {
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });
  delete keyBanks.left[0].dataset.action;
  delete keyBanks.right[0].dataset.action;
  const total = (tglData.targets || []).length;
  const maxPage = Math.max(0, Math.ceil(total / TGL_MAX_DISPLAY) - 1);
  if (tglPage > maxPage) tglPage = maxPage;
  if (tglPage < 0) tglPage = 0;
  placeOverlayLabel('left', 0, tglPage > 0 ? 'PREV' : 'MAIN', tglPage > 0 ? 'tgl-prev' : 'main');
  if (tglPage < maxPage) placeOverlayLabel('right', 0, 'NEXT', 'tgl-next');
}

// Apply a page's declared softkeys to one bezel zone (the declarative contract). The page emits
// pane-local 1-based row slots; the shell maps each to a physical key as
// keyBanks[side][paneOffset + slot] — paneOffset 0 = full view / split-top, 3 = split-bottom.
// Clears the row-key zone first (slots 1..maxRow both sides — nav on slot 0 is shell-owned) so a
// shrinking list releases its keys; maxRow = 5 in full view, 2 per split pane. An empty label
// binds the key with no visible overlay label (TGL's target row in the frame is the visual).
// Deselect needs no pane routing (it just sends a command), so no data-pane tag is set — split's
// dispatch falls through mfdButton's split branch to the shared 'target.deselect' switch case.
function applySoftkeys(keys, paneOffset, maxRow) {
  for (let s = 1; s <= maxRow; s++) {
    const lk = keyBanks.left[paneOffset + s], rk = keyBanks.right[paneOffset + s];
    if (lk) { delete lk.dataset.action; delete lk.dataset.id; }
    if (rk) { delete rk.dataset.action; delete rk.dataset.id; }
  }
  (keys || []).forEach(function(sk) {
    const bank = keyBanks[sk.side];
    const key = bank && bank[paneOffset + sk.slot];
    if (!key) return;
    key.dataset.action = sk.action;
    if (sk.data && sk.data.id != null) key.dataset.id = sk.data.id;
    if (sk.label) placeOverlayLabel(sk.side, paneOffset + sk.slot, sk.label, sk.action);
  });
}

// Slice the full target list to the page a given pane is scrolled to. Returns the visible rows
// plus whether PREV/NEXT exist, so renderSplitLabels can place the right nav labels. Clamps a
// stale page index (e.g. the target list shrank) back into range as a side effect.
function tglPaneSlice(idx) {
  const list = tglData.targets || [];
  const total = list.length;
  const maxPage = Math.max(0, Math.ceil(total / TGL_SPLIT_MAX) - 1);
  if (paneTglPage[idx] > maxPage) paneTglPage[idx] = maxPage;
  if (paneTglPage[idx] < 0)       paneTglPage[idx] = 0;
  const start = paneTglPage[idx] * TGL_SPLIT_MAX;
  const items = list.slice(start, start + TGL_SPLIT_MAX);
  return { items: items, hasPrev: paneTglPage[idx] > 0, hasNext: start + items.length < total,
           page: maxPage > 0 ? paneTglPage[idx] + 1 : 1, pages: maxPage + 1 };
}
function forwardTglToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgl') return;
    if (!iframe.contentWindow) return;
    const sl = tglPaneSlice(idx);
    iframe.contentWindow.postMessage(
      { mfd: true, type: 'tgl', items: sl.items, page: sl.page, pages: sl.pages }, '*');
  });
}
// Tell each TGL pane where its row slots should sit so the rows line up with the physical bezel
// keys flanking that pane. Slot order matches the pane's fill order: L1, L2 then R1, R2.
function forwardTglLayoutToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'tgl') return;
    if (!iframe.contentWindow) return;
    const paneTop = iframe.getBoundingClientRect().top;
    const off = idx === 0 ? 0 : 3;
    function cy(key) { const r = key.getBoundingClientRect(); return r.top + r.height / 2 - paneTop; }
    const slotYs = [
      cy(keyBanks.left[off + 1]),   // L1
      cy(keyBanks.left[off + 2]),   // L2
      cy(keyBanks.right[off + 1]),  // R1
      cy(keyBanks.right[off + 2]),  // R2
    ];
    iframe.contentWindow.postMessage({ mfd: true, type: 'tgl-layout', slotYs: slotYs }, '*');
  });
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
    else if (page === 'wpn')  { forwardWpnToPanes(); forwardCmToPanes(); forwardWpnLayoutToPanes(); }
    else if (page === 'tgl')  { forwardTglToPanes(); forwardTglLayoutToPanes(); }
  });
});

// Full-view frame load: push the current snapshot once it's ready (it may have started loading
// mid-update, or its src just switched between WPN/TGL), plus orientation + layout geometry.
pageFrame.addEventListener('load', function() {
  if (splitMode || !FRAME_PAGES[currentPage]) return;
  forwardOrientationToPane(pageFrame);
  if (currentPage === 'wpn')      { forwardWpnLayoutToFrame(); forwardWpnToFrame(); forwardCmToFrame(); }
  else if (currentPage === 'tgl') { forwardTglLayoutToFrame(); forwardTglToFrame(); }
  else if (currentPage === 'tgp') { forwardTgpToFrame(); }
  else if (currentPage === 'avn') { forwardAvnLayoutToFrame(); forwardAvnToFrame(); }
  else if (currentPage === 'rwr') { forwardRwrToFrame(); forwardMwToFrame(); }
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

function indicatorVisible(name) {
  if (name === 'pinned') return pinnedPage !== null && currentPage === pinnedPage;
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

// Latest target list mirrored from the map iframe. Whole list is kept in memory; the shell
// slices it to TGL_MAX_DISPLAY per page (the #page-frame TGL page renders the slice — left key
// 1..5 then right key 1..5) and pages through them with PREV/NEXT on the side keys (shell-owned
// nav, placeTglNavLabels). tglPage = 0-indexed full-view page.
let tglData = { targets: [] };
let tglPage = 0;
const TGL_MAX_DISPLAY = 10;

// Latest avionics snapshot mirrored from the map iframe. name = aircraft display name (also
// the key for /airframe + /airframe-layout); parts = the live HP list from the snapshot;
// failures = list of failure-message strings currently active (e.g. ["LEFT ENGINE FIRE"]).
// Latest AVN snapshot, mirrored from the map iframe's SSE feed. The shell keeps only this
// state (the forwarders read it); all rendering — silhouette, failure labels, FUEL/THROTTLE
// bars, the AVN_FAILURE_DEFS table, the /airframe layout cache — lives in src/web/pages/avn/.
let avnData = { name: null, parts: null, failures: null, fuel: -1, throttle: -1 };

// Latest RWR emitters + incoming missiles, mirrored from the map iframe's SSE feed. The shell
// keeps only this state (the forwarders read it); all scope SVG rendering lives in src/web/pages/rwr/.
let rwrData = { items: [] };
let mwData  = { items: [] };

function clearKeyActions() {
  // Only the page-dynamic banks (left/right) get cleared between pages. The top and bottom
  // banks hold page-independent controls (fullscreen on top; PIN, SWAP, layout… on bottom)
  // whose actions are wired once at startup and must survive page switches.
  ['left', 'right'].forEach(function(bank) {
    keyBanks[bank].forEach(function(k) {
      delete k.dataset.action;
      delete k.dataset.pane;     // split-mode tag; harmless to clear unconditionally
      delete k.dataset.id;       // target-deselect id (TGL page); clear so it never lingers
    });
  });
}

function placeOverlayLabel(bankName, keyIndex, label, action) {
  const side = bankName || 'left';
  const bank = keyBanks[side];
  const k = bank && bank[keyIndex];
  if (!k) return;

  if (action) k.dataset.action = action;
  const el = document.createElement('div');
  el.className = 'overlay-item ' + side;
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
}

// Render a page: set the overlay background, (re)assign key actions, and position
// each item label next to its physical key.
function showPage(name) {
  currentPage = name;
  const page = PAGES[name];
  overlayEl.classList.toggle('opaque', page.opaque);
  infoBox.classList.toggle('show', name === 'main');
  screenEl.classList.toggle('page-on', !!FRAME_PAGES[name]);   // WPN/TGL/TGP/AVN render in #page-frame
  clearKeyActions();
  // Only wipe dynamic line-select labels; static children (info-box) stay put.
  overlayEl.querySelectorAll('.overlay-item').forEach(function(el) { el.remove(); });

  page.items.forEach(function(item) {
    placeOverlayLabel(item.side || 'left', item.key, item.label, item.action);
  });

  // TGL and WPN own their own nav labels (PREV/MAIN + NEXT) because they depend on the page
  // state; run after the generic label sweep so they don't get clobbered. Both render in
  // #page-frame: point it at the page (switching src as needed) then forward layout + data.
  // Their per-item keys differ: WPN has none (nav only); TGL's per-target deselect keys arrive
  // via the softkey contract the frame posts up (see the 'softkeys' handler).
  if (name === 'wpn') {
    showFramePage('wpn');
    placeWpnNavLabels();                                          // MAIN/PREV/NEXT (shell-owned)
    forwardWpnLayoutToFrame(); forwardWpnToFrame(); forwardCmToFrame();
  }
  if (name === 'tgl') {
    showFramePage('tgl');
    placeTglNavLabels();                                          // MAIN/PREV/NEXT (shell-owned)
    forwardTglLayoutToFrame(); forwardTglToFrame();
  }
  // TGP renders in #page-frame too. Its only key is the static MAIN label (PAGES.tgp.items,
  // placed by the generic sweep above), so there's no nav/softkey wiring — just forward the
  // lock flag. The page connects to /tgp.mjpg itself once loaded.
  if (name === 'tgp') {
    showFramePage('tgp');
    forwardTgpToFrame();
  }
  // AVN renders in #page-frame too. Its only key is the static MAIN label (PAGES.avn.items,
  // placed by the generic sweep above); forward the bezel geometry (full profile) + snapshot.
  if (name === 'avn') {
    showFramePage('avn');
    forwardAvnLayoutToFrame(); forwardAvnToFrame();
  }
  // RWR renders in #page-frame too. Its only key is the static MAIN label (PAGES.rwr.items,
  // placed by the generic sweep above); forward the contact + missile snapshots.
  if (name === 'rwr') {
    showFramePage('rwr');
    forwardRwrToFrame(); forwardMwToFrame();
  }

  renderIndicators();
}

// The map iframe broadcasts status + loadout + cm via postMessage; mirror onto the
// info-box (MAIN page), the cached wpnData + cmData (WPN page).
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  // Softkeys come UP from a hosted page frame (not the map), so handle them before the mapFrame
  // source guard. The page emits pane-local slots; the shell maps them to physical keys per zone.
  //   full view  → the #page-frame, offset 0, rows on slots 1..5.
  //   split view → the emitting pane, offset 0 (top) / 3 (bot), rows on slots 1..2. Each pane's
  //                set is cached so renderSplitLabels can re-apply it (a re-render of the OTHER
  //                pane clears all keys; this pane may not re-emit).
  if (m.type === 'softkeys') {
    if (!splitMode && e.source === pageFrame.contentWindow && FRAME_PAGES[currentPage]) {
      applySoftkeys(m.keys, 0, 5);
    } else if (splitMode) {
      const idx = paneIframes.findIndex(function(f) { return f.contentWindow === e.source; });
      if (idx >= 0) { paneSoftkeys[idx] = m.keys || []; applySoftkeys(paneSoftkeys[idx], idx === 0 ? 0 : 3, 2); }
    }
    return;
  }
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
    // Mirror the full target list. The frame slices to TGL_MAX_DISPLAY; if any of the first 10
    // got deselected, the next held-back targets slide in on the next render.
    tglData = { targets: Array.isArray(m.items) ? m.items : [] };
    // Full-view: re-forward the slice to the frame (it re-renders + re-emits its softkeys) and
    // refresh the nav labels (target count can add/remove pages, changing PREV/NEXT visibility).
    if (currentPage === 'tgl' && !splitMode) { forwardTglToFrame(); placeTglNavLabels(); }
    // Target count can add/remove pages, so refresh each TGL pane's slice + PREV/NEXT labels.
    if (splitMode) { forwardTglToPanes(); renderSplitLabels(); }
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
  }
});

// Drive the map iframe without reaching into it (keeps the map a standalone component;
// also works cross-origin under file://).
function mapSend(action) {
  if (mapFrame && mapFrame.contentWindow)
    mapFrame.contentWindow.postMessage({ mfd: true, action: action }, '*');
}

// Replay the CRT power-on flicker (≤2s, capped by the 1.6s animation). Re-arming requires
// clearing the class and forcing a reflow so the animation restarts from 0% each time.
function flickerScreen() {
  screenEl.classList.remove('powering-on');
  void screenEl.offsetWidth;                 // reflow — restart the animation
  screenEl.classList.add('powering-on');
  setTimeout(function() { screenEl.classList.remove('powering-on'); }, 1100);
}

// Boot loader for the centre info box: shows a LOADING… line + a fill bar, keeps the title
// visible, hides the data rows until the bar hits 100%. Runs alongside the power-on /
// first-load flicker. Fills in 5% steps every 50ms = 1.0s (the 60ms CSS transition on the
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
// the TGL list) come back via normal telemetry, so the shell's calls are fire-and-forget: add
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
    } else if (act === 'tgl-prev' || act === 'tgl-next') {
      paneTglPage[paneIdx] += (act === 'tgl-next' ? 1 : -1);
      forwardTglToPanes();
      renderSplitLabels();
    } else if (act === 'flw' || act === 'zin' || act === 'zout') {
      // MAP controls act on the pane's own map iframe — they don't navigate it away.
      paneMapSend(paneIdx, act === 'flw' ? 'toggle-follow' : act === 'zin' ? 'zoom-in' : 'zoom-out');
    } else {
      paneNavigate(paneIdx, act);
    }
    return;
  }
  // Note: TGL deselect keys (action 'target.deselect') carry NO data-pane tag, so they skip the
  // block above and fall through to the switch below — the deselect command is pane-independent.

  switch (el.dataset.action) {
    case 'main': showPage('main'); mapSend('status-request'); break;   // pull fresh status on open
    case 'map':  showPage('map');  break;
    case 'wpn':       wpnPage = Math.max(0, selWpnPageFull()); showPage('wpn'); break;   // open on the selected weapon's page
    case 'wpn-prev':  wpnPage--;   showPage('wpn'); break;   // renderWpn clamps on overshoot
    case 'wpn-next':  wpnPage++;   showPage('wpn'); break;
    case 'tgp':  showPage('tgp');  break;
    case 'tgl':       tglPage = 0; showPage('tgl'); break;   // fresh entry — always start on page 0
    case 'tgl-prev':  tglPage--;   showPage('tgl'); break;   // forwardTglToFrame clamps overshoot
    case 'tgl-next':  tglPage++;   showPage('tgl'); break;
    case 'target.deselect':                                  // softkey-contract deselect (full + split)
      if (el.dataset.id) sendCommand('target.deselect', { id: +el.dataset.id }).catch(function() {});
      break;
    case 'avn':  showPage('avn');  break;
    case 'rwr':  showPage('rwr');  break;
    case 'flw':  mapSend('toggle-follow'); break;
    case 'zin':  mapSend('zoom-in');  break;
    case 'zout': mapSend('zoom-out'); break;
    case 'power': {
      // Black out the screen recess (bezel stays lit). Powering back ON always boots
      // into full view — never resumes a split that was active when it went dark.
      const turningOn = screenEl.classList.contains('power-off');
      screenEl.classList.toggle('power-off');
      if (turningOn) {
        // Power-on always boots into full-view MAIN — never resumes a split or the page
        // that was showing when it went dark.
        if (splitMode) { splitMode = false; applySplitMode(); }
        currentPage = 'main';
        showPage('main');
        mapSend('status-request');
        flickerScreen();
        runBootLoading();
      }
      break;
    }
    case 'fll':  toggleFullscreen(); break;
    case 'split':
      // One-way: enter split if not already. Pressing 2×1 while already split is a no-op.
      // Collapse back to single uses the dedicated square (1×1) button below.
      if (splitMode) break;
      splitMode = true;
      // Carry the full-view page into the TOP pane; the BOTTOM pane defaults to MAIN.
      // Pages without a bare iframe version yet (no PAGE_URL entry) fall back to MAIN
      // so the top pane is never blank.
      panePages = [PAGE_URL[currentPage] ? currentPage : 'main', 'main'];
      applySplitMode();
      break;
    case 'unsplit':
      // One-way: collapse split back to single. No-op if already in single mode.
      // The full-screen pane adopts whatever the TOP pane was showing.
      if (!splitMode) break;
      splitMode = false;
      currentPage = panePages[0];
      applySplitMode();
      break;
    case 'swap':
      // Toggle between the pinned page and the last page we swapped from.
      //   - On a non-pinned page: remember it as the partner, jump to pinned.
      //   - On the pinned page with a known partner: jump back to the partner.
      //   - Otherwise (nothing pinned, or on pinned with no partner yet): no-op.
      if (pinnedPage === null) break;
      if (currentPage === pinnedPage) {
        if (swapPartner === null) break;
        showPage(swapPartner);
      } else {
        swapPartner = currentPage;
        showPage(pinnedPage);
      }
      break;
    case 'pin':
      // MENU ('main') page is not pinnable per design.
      if (currentPage === 'main') break;
      if (pinnedPage === currentPage) {
        // Toggle off: unpin and drop the chip from the activation order.
        pinnedPage = null;
        indicatorOrder = indicatorOrder.filter(function(x) { return x !== 'pinned'; });
      } else {
        // First time on, or switching the pin to a new page: append so we land to the
        // LEFT of any chip that was activated earlier (FOLLOW), and to the right of any
        // chip activated later in the same session.
        pinnedPage = currentPage;
        if (indicatorOrder.indexOf('pinned') === -1) indicatorOrder.push('pinned');
      }
      // The partner is tied to the previous pin — drop it whenever the pin itself
      // changes so a fresh SWAP cycle starts from the next non-pinned page.
      swapPartner = null;
      renderIndicators();
      break;
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
  if (splitMode) { renderSplitLabels(); forwardWpnLayoutToPanes(); forwardTglLayoutToPanes(); }
  else           showPage(currentPage);
});
loadConfigUrls();
showPage('main');   // start on the MAIN page
flickerScreen();    // CRT power-on flicker on first load
runBootLoading();   // boot loader in the centre info box on first load
