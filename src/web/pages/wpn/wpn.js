// WPN page — a pure reactive renderer driven by the shell over postMessage. Two layout
// profiles share this one file:
//   compact : split-pane / half-height. Weapons in 4 corners (L1,L2,R1,R2), no weapon image.
//             Positioned by the forwarded slotYs (vertical centres). The default.
//   full    : single full-screen iframe. Weapons as one left column spanning each line-select
//             key (slot top+height forwarded as `slots`), with a big selected-weapon image on
//             the right ~45% (geometry forwarded as `iconTop`/`iconHeight`). Enabled by
//             layout:'full' in the wpn-layout message (adds body.full).
// See wpn.html for the message contract (wpn / wpn-layout / cm / orient).

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const wpnPanel    = document.getElementById('wpn-panel');
const cmPanel     = document.getElementById('cm-panel');
const cmFlaresTitle = document.getElementById('cm-flares-title');
const cmJammerTitle = document.getElementById('cm-jammer-title');
const cmFlaresVal   = document.getElementById('cm-flares-val');
const cmJammerVal   = document.getElementById('cm-jammer-val');
const cmFlaresIcon  = document.getElementById('cm-flares-icon');
const cmBarFill     = document.getElementById('cm-bar-fill');
const flareDots     = Array.prototype.slice.call(document.querySelectorAll('.flare-dot'));
const pageInd       = document.getElementById('page-ind');
const wpSelIconWrap = document.getElementById('wp-sel-icon-wrap');
const wpSelIconImg  = document.getElementById('wp-sel-icon');

// ── State ──────────────────────────────────────────────────────────────────────────
// wpnData.items is already the shell-sliced page; this page never paginates.
// compact: slotYs[i] = pane-local vertical centre, fill order L1,L2,R1,R2; side(i)='left' i<2.
// full:    fullSlots[i] = {top,height} of weapon slot i down the left column (L1..L5);
//          iconArea = {top,height} of the right-half weapon-image box.
// cmBand = {top,height} of the first key band (both profiles).
let wpnData = { items: [], selWeapon: null };
let cmData  = { flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 };
let layout  = 'compact';
let slotYs  = null;
let sides   = null;          // compact: per-row side class forwarded by the shell (H = L,L,R,R; V = one side)
let fullSlots = null;
let iconArea  = null;
let cmBand  = null;
let listSide = null;         // in V_SPLIT: the side the single-column list sits on ('left'/'right'); null otherwise
const isVsplit = function() { return listSide != null; };
let wpnKey  = '';            // layout + names; rebuild rows when either changes
let wpnItemEls  = [];
let wpSelIconKey = null;     // last weapon name pushed to the image src

const isFull = function() { return layout === 'full'; };

function slotSide(i) { return i < 2 ? 'left' : 'right'; }
// The side a row is drawn on: the shell-forwarded per-row side when present (so V_SPLIT can put
// every row on the pane's own column), else the default 2×2 fill.
function itemSide(i) { return (sides && sides[i]) ? sides[i] : slotSide(i); }
// Compact fallback positions used until the shell forwards real geometry. The weapon keys
// flanking a pane (skipping the top band) sit at ~1/2 and ~5/6 of pane height; L1,L2,R1,R2.
function fallbackY(i) {
  const h = window.innerHeight;
  return h * [0.5, 0.833, 0.5, 0.833][i];
}
function slotY(i) { return (slotYs && typeof slotYs[i] === 'number') ? slotYs[i] : fallbackY(i); }

function applyCmBand() {
  if (cmBand && typeof cmBand.top === 'number') {
    cmPanel.style.top = cmBand.top + 'px';
    cmPanel.style.height = Math.max(0, cmBand.height) + 'px';
    cmPanel.style.transform = 'translateX(-50%)';   // band already gives the vertical extent
    sizeCm();
  }
}

// Position the full-profile weapon-image box over the keys-1..5 span (full view only).
function applyIconArea() {
  if (iconArea && typeof iconArea.top === 'number') {
    wpSelIconWrap.style.top    = iconArea.top + 'px';
    wpSelIconWrap.style.height = Math.max(0, iconArea.height) + 'px';
  }
}

// Size the big readouts + IR icon to the band height so the count/kJ read big and stay
// clustered. Grid rows are auto-sized (no 1fr), so the glyph height is derived from the slot
// directly. Only runs once the band height is known (set by applyCmBand) to avoid a
// font-size/height feedback loop on the auto-height fallback.
function sizeCm() {
  if (!cmPanel.style.height) return;
  // Columns: `1fr 1px 1fr` with 14px column-gap, so each column track is:
  const colW  = Math.max(0, (cmPanel.clientWidth - 1 - 14 * 2) / 2);
  const slotH = cmPanel.getBoundingClientRect().height;
  if (slotH < 4 || colW < 4) return;
  const targetH  = slotH * 0.55;
  const iconSize = Math.max(0, Math.min(targetH, colW * 0.5));
  cmFlaresIcon.style.width  = iconSize + 'px';
  cmFlaresIcon.style.height = iconSize + 'px';
  function fitText(el, maxH, maxW) {
    if (maxH < 4 || maxW < 4) return;
    let size = Math.floor(maxH * 0.8);
    el.style.fontSize = size + 'px';
    const w = el.scrollWidth;
    if (w > maxW && w > 0) {
      size = Math.max(8, Math.floor(size * maxW / w));
      el.style.fontSize = size + 'px';
    }
  }
  const flaresUsableW = Math.max(0, colW - iconSize - 10);   // 10 = CSS gap
  fitText(cmFlaresVal, targetH, flaresUsableW);
  fitText(cmJammerVal, targetH, colW);
  // Match the capacitor bar width to the kJ readout so they read as one grouped unit.
  cmBarFill.parentElement.style.width = Math.ceil(cmJammerVal.getBoundingClientRect().width) + 'px';
}

// Place weapon row i: compact pins its vertical centre to slotY(i); full spans the
// forwarded slot rectangle (top+height) so the row fills its line-select key band.
function positionRow(i, el) {
  if (isFull()) {
    const s = (fullSlots && fullSlots[i]) || null;
    if (s) { el.style.top = s.top + 'px'; el.style.height = Math.max(0, s.height) + 'px'; }
  } else {
    el.style.top = slotY(i) + 'px';
    el.style.height = '';
  }
}

// ── Weapon list renderer ─────────────────────────────────────────────────────────────
function renderWpn() {
  const list = wpnData.items || [];
  wpnPanel.classList.toggle('has-loadout', list.length > 0);

  // Rebuild rows when the layout profile, per-row sides, or the name list changes (the side class
  // differs between profiles/orientations, so a flip must rebuild even if the names are identical).
  const key = layout + '|' + (sides ? sides.join(',') : '') + '||' + list.map(function(w) { return w.n; }).join('|');
  if (key !== wpnKey) {
    wpnKey = key;
    wpnItemEls = [];
    wpnPanel.querySelectorAll('.wp-item').forEach(function(el) { el.remove(); });
    list.forEach(function(w, i) {
      const item = document.createElement('div');
      item.className = isFull() ? 'wp-item' : ('wp-item ' + itemSide(i));
      const name = document.createElement('div');
      name.className = 'wp-name';
      name.textContent = w.n;
      item.appendChild(name);
      const ammo = document.createElement('div');
      ammo.className = 'wp-ammo';
      item.appendChild(ammo);
      wpnPanel.appendChild(item);
      wpnItemEls.push({ item: item, ammo: ammo });
    });
  }

  for (let i = 0; i < list.length && i < wpnItemEls.length; i++) {
    const w = list[i];
    const el = wpnItemEls[i];
    positionRow(i, el.item);
    el.ammo.innerHTML = (w.f > 0) ? ('<span>' + w.a + '</span> / ' + w.f) : '';
    el.item.classList.toggle('sel',   w.n === wpnData.selWeapon);
    el.item.classList.toggle('empty', w.f > 0 && w.a === 0);
  }

  renderSelIcon();
}

// Full-profile only: the big image of the selected weapon on the right half. Swaps src only
// when the selection changes; hidden in compact and whenever nothing is selected.
function renderSelIcon() {
  const sel = wpnData.selWeapon;
  if ((isFull() || isVsplit()) && sel && (wpnData.items || []).length) {
    if (sel !== wpSelIconKey) {
      wpSelIconKey = sel;
      wpSelIconImg.style.visibility = '';
      wpSelIconImg.src = '/weapon?name=' + encodeURIComponent(sel);
    }
    wpSelIconWrap.classList.add('show');
  } else {
    wpSelIconWrap.classList.remove('show');
    wpSelIconKey = null;
  }
}
wpSelIconImg.onerror = function() { wpSelIconImg.style.visibility = 'hidden'; };
wpSelIconImg.onload  = function() { wpSelIconImg.style.visibility = ''; };

function repositionRows() {
  for (let i = 0; i < wpnItemEls.length; i++) positionRow(i, wpnItemEls[i].item);
}

// Bottom-right "PAGE x/y" box. Hidden unless the loadout spans more than one page — a
// single-page loadout has nowhere to navigate, so no indicator is shown.
function updatePageInd(page, pages) {
  if (pages > 1) {
    pageInd.textContent = 'PAGE ' + page + '/' + pages;
    pageInd.classList.remove('empty');
  } else {
    pageInd.classList.add('empty');
  }
}

// ── Countermeasures renderer ─────────────────────────────────────────────────────────
function renderCm() {
  cmFlaresVal.textContent = (cmData.flares >= 0) ? cmData.flares : '—';
  if (cmData.ewKJ >= 0) {
    cmJammerVal.innerHTML = '<span>' + Math.round(cmData.ewKJ) + '</span><span>kJ</span>';
  } else {
    cmJammerVal.textContent = '—';
  }

  const pct = (cmData.ewKJMax > 0 && cmData.ewKJ >= 0)
            ? Math.max(0, Math.min(1, cmData.ewKJ / cmData.ewKJMax))
            : 0;
  cmBarFill.style.width = (pct * 100) + '%';

  const flaresEmpty = cmData.flaresMax > 0 && cmData.flares === 0;
  const jammerEmpty = cmData.ewKJMax  > 0 && cmData.ewKJ   === 0;
  cmFlaresTitle.classList.toggle('sel',   cmData.cmCat === 1);
  cmFlaresTitle.classList.toggle('empty', flaresEmpty);
  cmFlaresVal  .classList.toggle('empty', flaresEmpty);
  cmFlaresIcon .classList.toggle('empty', flaresEmpty);

  const knowFlares = !flaresEmpty && cmData.flaresMax > 0 && cmData.flares >= 0;
  const spentDots  = knowFlares
    ? Math.floor((cmData.flaresMax - cmData.flares) * flareDots.length / cmData.flaresMax)
    : 0;
  flareDots.forEach(function(d, i) { d.classList.toggle('spent', i < spentDots); });
  cmJammerTitle.classList.toggle('sel',   cmData.cmCat === 2);
  cmJammerTitle.classList.toggle('empty', jammerEmpty);
  cmJammerVal  .classList.toggle('empty', jammerEmpty);

  sizeCm();   // re-fit the readouts now that their text content has changed
}

// ── Shell → page forwarding ────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'wpn') {
    wpnData = { items: Array.isArray(m.items) ? m.items : [], selWeapon: m.selWeapon || null };
    updatePageInd(typeof m.page === 'number' ? m.page : 1, typeof m.pages === 'number' ? m.pages : 1);
    renderWpn();
  } else if (m.type === 'wpn-layout') {
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', isFull());
    slotYs    = Array.isArray(m.slotYs) ? m.slotYs : null;
    sides     = Array.isArray(m.sides)  ? m.sides  : null;
    fullSlots = Array.isArray(m.slots)  ? m.slots  : null;
    iconArea  = (typeof m.iconTop === 'number') ? { top: m.iconTop, height: m.iconHeight } : null;
    cmBand    = (typeof m.cmTop  === 'number') ? { top: m.cmTop,  height: m.cmHeight } : null;
    listSide  = (m.listSide === 'left' || m.listSide === 'right') ? m.listSide : null;
    // V_SPLIT shows the weapon image opposite the list (body.vsplit + list-<side>); cleared otherwise.
    document.body.classList.toggle('vsplit', isVsplit());
    document.body.classList.toggle('list-left',  listSide === 'left');
    document.body.classList.toggle('list-right', listSide === 'right');
    applyCmBand();
    applyIconArea();
    renderWpn();   // re-render: a profile flip changes row classes + the image
  } else if (m.type === 'cm') {
    cmData = {
      flares:    typeof m.flares    === 'number' ? m.flares    : -1,
      flaresMax: typeof m.flaresMax === 'number' ? m.flaresMax : -1,
      ewKJ:      typeof m.ewKJ      === 'number' ? m.ewKJ      : -1,
      ewKJMax:   typeof m.ewKJMax   === 'number' ? m.ewKJMax   : -1,
      cmCat:     m.cmCat || 0,
    };
    renderCm();
  } else if (m.type === 'orient') {
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});

window.addEventListener('resize', function() { repositionRows(); applyIconArea(); sizeCm(); });

renderWpn();   // initial empty-state paint
renderCm();
