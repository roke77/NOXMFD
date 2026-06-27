// TGL page — a pure reactive renderer driven by the shell over postMessage. Single source of
// truth for the target list in BOTH layouts (the full-view overlay in MfdPage.cs is gone). Two
// profiles share this file:
//   compact : split-pane / half-height. Up to 4 targets (L1,L2,R1,R2), vertical centre pinned
//             to the forwarded slotYs. Font sizes via CSS clamp(). The default.
//   full    : single full-screen iframe. Up to 10 targets in two columns (L1..L5, R1..R5), each
//             row spanning its line-select key band (slot {top,height} forwarded as `slots`);
//             font sizes computed here from the slot height (shrink-to-fit). Enabled by
//             layout:'full' in the tgl-layout message (adds body.full).
// The page also emits the declarative softkey contract up to the shell: one
// {side, slot, action:'target.deselect', data:{id}} per visible target, with a pane-local
// 1-based row slot. The shell maps slot+paneOffset to the physical bezel and dispatches the
// deselect on click — so the per-target key binding lives in ONE place for full + split.
// See tgl.html for the full message contract.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const tglPanel = document.getElementById('tgl-panel');
const pageInd  = document.getElementById('page-ind');

// ── State ──────────────────────────────────────────────────────────────────────────
// tglData.targets is already the shell-sliced page (<= 4 compact / <= 10 full); never paginates.
// compact: slotYs[i] = pane-local vertical centre, fill order L1,L2,R1,R2.
// full:    fullSlots[j] = {top,height} of vertical key band j (0..4); BOTH columns share them.
let tglData   = { targets: [] };
let layout    = 'compact';
let slotYs    = null;
let fullSlots = null;
let tglKey    = '';
let tgItemEls = [];

const isFull = function() { return layout === 'full'; };
function rowsPerSide() { return isFull() ? 5 : 2; }
function slotSide(i)   { return i < rowsPerSide() ? 'left' : 'right'; }
function localIdx(i)   { return i < rowsPerSide() ? i : i - rowsPerSide(); }   // 0-based slot in column

// Compact fallback positions used until the shell forwards real geometry. The row keys flanking
// a pane (skipping the top band) sit at ~1/2 and ~5/6 of pane height; slot order L1, L2, R1, R2.
function fallbackY(i) {
  const h = window.innerHeight;
  return h * [0.5, 0.833, 0.5, 0.833][i];
}
function slotY(i) { return (slotYs && typeof slotYs[i] === 'number') ? slotYs[i] : fallbackY(i); }

// Format range as "8,4 km" (European decimal comma) when given a number; pass strings through.
function fmtRng(r) {
  if (typeof r === 'number' && isFinite(r)) return r.toFixed(1).replace('.', ',') + ' km';
  return (r != null ? String(r) : '—');
}

// Position + size row i. compact pins the vertical centre to slotY(i) (CSS translateY(-50%));
// full spans the forwarded slot rectangle (top+height) and sizes the fonts to the slot height.
function positionRow(i, el) {
  if (isFull()) {
    const s = (fullSlots && fullSlots[localIdx(i)]) || null;
    if (!s) return;
    const sideW = Math.max(40, window.innerWidth * 0.5);
    el.item.style.top    = s.top + 'px';
    el.item.style.height = Math.max(0, s.height) + 'px';
    el.item.style.width  = sideW + 'px';
    sizeFullRow(el, s.height);
  } else {
    el.item.style.top    = slotY(i) + 'px';
    el.item.style.height = '';
    el.item.style.width  = '';
  }
}

// Full-profile font sizing: name is 5/3 the meta size, both scaled to the slot height, then
// shrunk to fit the column width if the widest line overflows. Mirrors the former overlay
// renderTgl. (Compact relies on CSS clamp() and does nothing here.)
function sizeFullRow(el, slotH) {
  let metaPx = Math.max(8, slotH * 0.115);
  let namePx = metaPx * (5 / 3);
  el.name.style.fontSize = namePx.toFixed(1) + 'px';
  el.grid.style.fontSize = metaPx.toFixed(1) + 'px';
  el.rng .style.fontSize = metaPx.toFixed(1) + 'px';
  const avail = el.item.clientWidth;
  if (avail > 0) {
    const widest = Math.max(el.name.scrollWidth, el.grid.scrollWidth, el.rng.scrollWidth);
    if (widest > avail) {
      const k = avail / widest;
      namePx *= k; metaPx *= k;
      el.name.style.fontSize = namePx.toFixed(1) + 'px';
      el.grid.style.fontSize = metaPx.toFixed(1) + 'px';
      el.rng .style.fontSize = metaPx.toFixed(1) + 'px';
    }
  }
}

// ── Target list renderer ─────────────────────────────────────────────────────────────
function renderTgl() {
  const list = tglData.targets || [];
  tglPanel.classList.toggle('has-targets', list.length > 0);

  // Rebuild rows when the layout profile or the set of target names changes (the side class
  // depends on rowsPerSide(), so a profile flip must rebuild even if the names match).
  const key = layout + '||' + list.map(function(t) { return t.n; }).join('|');
  if (key !== tglKey) {
    tglKey = key;
    tgItemEls = [];
    tglPanel.querySelectorAll('.tg-item').forEach(function(el) { el.remove(); });
    list.forEach(function() {
      const item = document.createElement('div');
      const name = document.createElement('div'); name.className = 'tg-name'; item.appendChild(name);
      const grid = document.createElement('div'); grid.className = 'tg-grid'; item.appendChild(grid);
      const rng  = document.createElement('div'); rng.className  = 'tg-rng';  item.appendChild(rng);
      tglPanel.appendChild(item);
      tgItemEls.push({ item: item, name: name, grid: grid, rng: rng });
    });
  }

  for (let i = 0; i < list.length && i < tgItemEls.length; i++) {
    const t  = list[i];
    const el = tgItemEls[i];
    // Faction class: 1 = friendly (blue), 0 = neutral (white), anything else = enemy (red).
    const factionCls = t.f === 1 ? ' f-friendly' : t.f === 0 ? ' f-neutral' : '';
    el.item.className   = 'tg-item ' + slotSide(i) + factionCls;
    el.name.textContent = t.n || '—';
    el.grid.textContent = 'GRID: ' + (t.g != null ? String(t.g) : '—');
    el.rng.textContent  = 'RNG: ' + fmtRng(t.r);
    positionRow(i, el);
  }

  emitSoftkeys();
}

// Declarative softkey contract: one deselect key per visible target, posted up to the shell.
// slot is the pane-local 1-based row key (L1..L5 / R1..R5 in full, L1,L2,R1,R2 in compact); the
// shell adds paneOffset to reach the physical key. Empty label: the target row IS the visual.
function emitSoftkeys() {
  const list = tglData.targets || [];
  const keys = [];
  for (let i = 0; i < list.length; i++) {
    const t = list[i];
    if (!t || t.id == null) continue;
    keys.push({ side: slotSide(i), slot: localIdx(i) + 1, label: '',
                action: 'target.deselect', data: { id: t.id } });
  }
  parent.postMessage({ mfd: true, type: 'softkeys', keys: keys }, '*');
}

function repositionRows() {
  for (let i = 0; i < tgItemEls.length; i++) positionRow(i, tgItemEls[i]);
}

// Bottom-right "PAGE x/y" box. Hidden unless the target list spans more than one page — a
// single-page list has nowhere to navigate, so no indicator is shown.
function updatePageInd(page, pages) {
  if (pages > 1) {
    pageInd.textContent = 'PAGE ' + page + '/' + pages;
    pageInd.classList.remove('empty');
  } else {
    pageInd.classList.add('empty');
  }
}

// ── Shell → page forwarding ────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgl') {
    tglData = { targets: Array.isArray(m.items) ? m.items : [] };
    updatePageInd(typeof m.page === 'number' ? m.page : 1, typeof m.pages === 'number' ? m.pages : 1);
    renderTgl();
  } else if (m.type === 'tgl-layout') {
    layout    = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', isFull());
    slotYs    = Array.isArray(m.slotYs) ? m.slotYs : null;
    fullSlots = Array.isArray(m.slots)  ? m.slots  : null;
    renderTgl();   // re-render: a profile flip changes row classes + sizing
  } else if (m.type === 'orient') {
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});

window.addEventListener('resize', function() { renderTgl(); });

renderTgl();   // initial empty-state paint
