namespace NORoksMFD
{
    // Bare WPN page served at /wpn. Used ONLY by the split-screen layout (single-pane WPN is
    // rendered by the shell's own overlay panel), so its layout is tuned for a half-height
    // pane: the countermeasures panel fills the top "first band" (the key slot beside MAIN),
    // and the weapon rows hug the left and right edges, vertically aligned to the side bezel
    // keys flanking the pane. There is NO selected-weapon image here — the pane is too short.
    //
    // The shell drives everything via postMessage and this page is a pure reactive renderer:
    //   - 'wpn'        : the (already-sliced, <= 4) weapon list + selected weapon.
    //   - 'wpn-layout' : geometry from the shell — slotYs (vertical centre of each weapon
    //                    slot, fill order L1, L2, R1, R2) and the CM band (cmTop + cmHeight)
    //                    so rows + the CM panel line up with the physical bezel. Falls back to
    //                    even positions until the first message arrives.
    //   - 'cm'         : countermeasures snapshot (flares + jammer).
    //   - 'orient'     : app-wide orientation (kept for parity).
    //
    // L0 (top-left) is the shell-owned MAIN back-button; R0 (top-right) is reserved for the
    // shell-owned NEXT label when the loadout has more than 4 weapons — so weapons never use
    // the top band, leaving it entirely to the CM panel.
    internal static class WpnPage
    {
        public const string Html = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NO Roks MFD — WPN</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
  body {
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    position: relative;
  }
  .wpn-panel { position: absolute; inset: 0; }
  .wpn-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-size: 22px;
    letter-spacing: 3px;
    pointer-events: none;
  }
  .wpn-panel.has-loadout .wpn-empty { display: none; }

  /* Countermeasures panel — fills the top "first band" (key slot beside MAIN). top + height
     are set by JS from the shell-forwarded band geometry; the middle grid row (1fr) grows so
     the flares count/icon and jammer readout fill the band's height. */
  .cm-panel {
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    top: 8px;                       /* overridden by JS once the band geometry arrives */
    width: 58%;
    max-width: 460px;
    display: none;
    grid-template-columns: 1fr 1px 1fr;
    grid-template-rows: auto 1fr auto;
    column-gap: 16px;
    row-gap: 4px;
    box-sizing: border-box;
    padding: 4px 0;
  }
  .wpn-panel.has-loadout .cm-panel { display: grid; }
  .cm-title { font-size: clamp(13px, 2.2vh, 20px); font-weight: 900; letter-spacing: 1px; white-space: nowrap; }
  .cm-title .cm-label { padding: 0 6px; }
  .cm-flares-title { grid-column: 1; grid-row: 1; text-align: right; }
  .cm-flares-body {
    grid-column: 1; grid-row: 2;
    min-height: 0;
    display: flex;
    align-items: center;
    justify-content: flex-end;     /* icon hugs the right edge; count sits left of it */
    gap: 10px;
  }
  .cm-flares-icon {
    flex: 0 0 auto;
    min-height: 0; min-width: 0;
    height: 100%;
    max-height: 72px;
    aspect-ratio: 1 / 1;
    color: #39ff14;
    display: flex;
    align-items: center;
  }
  .cm-flares-icon.empty { color: #ff4040; }
  .cm-flares-svg { display: block; width: 100%; height: 100%; }
  .cm-flares-svg .flare-dot.spent { stroke: #1a4a1a; }
  .cm-sep { grid-column: 2; grid-row: 1 / span 3; width: 1px; background: #1a4a1a; }
  .cm-jammer-title { grid-column: 3; grid-row: 1; }
  .cm-jammer-bar { grid-column: 3; grid-row: 3; align-self: center; justify-self: start; }
  .cm-big {
    min-height: 0; min-width: 0;
    font-weight: 900;
    letter-spacing: 1px;
    color: #39ff14;
    line-height: 1;
    display: flex;
    align-items: center;
    white-space: nowrap;
    font-size: clamp(20px, 4vh, 38px);
  }
  #cm-flares-val { flex: 0 0 auto; justify-content: flex-end; }
  #cm-jammer-val {
    grid-column: 3; grid-row: 2;
    align-self: center;
    justify-content: flex-start;
    width: fit-content;
    gap: 8px;
  }
  .cm-title.empty .cm-label,
  .cm-big.empty             { color: #ff4040; }
  .cm-title.sel       .cm-label { background: #39ff14; color: #060a06; }
  .cm-title.empty.sel .cm-label { background: #ff4040; color: #060a06; }
  .cm-bar {
    width: 100%;
    height: 12px;
    border: 1px solid #39ff14;
    border-radius: 3px;
    background: rgba(57, 255, 20, 0.08);
    box-sizing: border-box;
    overflow: hidden;
  }
  .cm-bar-fill {
    width: 0%;
    height: 100%;
    background: #39ff14;
    transition: width 120ms linear;
  }

  /* Weapon rows — absolutely positioned, vertical centre set by JS to the matching bezel
     key. .left hugs the left edge (left-aligned), .right hugs the right edge (right-aligned). */
  .wp-item {
    position: absolute;
    transform: translateY(-50%);
    display: flex;
    flex-direction: column;
    max-width: 46%;
  }
  .wp-item.left  { left: 16px;  align-items: flex-start; text-align: left; }
  .wp-item.right { right: 16px; align-items: flex-end;   text-align: right; }
  .wp-name {
    max-width: 100%;
    padding: 0 6px;
    font-size: clamp(13px, 2.8vh, 24px); font-weight: 900; letter-spacing: 1px;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .wp-item.left .wp-name { margin-left: -6px; }
  .wp-item.right .wp-name { margin-right: -6px; }
  .wp-ammo { font-size: clamp(11px, 2.1vh, 18px); color: #4aaa4a; letter-spacing: 1px; margin-top: 2px; padding: 0 6px; }
  .wp-ammo span { color: #39ff14; font-weight: 900; }
  .wp-item.empty .wp-name,
  .wp-item.empty .wp-ammo,
  .wp-item.empty .wp-ammo span { color: #ff4040; }
  .wp-item.sel .wp-name             { background: #39ff14; color: #060a06; }
  .wp-item.empty.sel .wp-name       { background: #ff4040; color: #060a06; }
</style>
</head>
<body>
  <div class="wpn-panel" id="wpn-panel">
    <div class="wpn-empty" id="wpn-empty">&mdash; NO LOADOUT &mdash;</div>
    <div class="cm-panel" id="cm-panel">
      <div class="cm-title cm-flares-title" id="cm-flares-title"><span class="cm-label">IR Flares</span></div>
      <div class="cm-flares-body">
        <div class="cm-big" id="cm-flares-val">&mdash;</div>
        <div class="cm-flares-icon" id="cm-flares-icon">
          <svg class="cm-flares-svg" viewBox="0 0 100 100" preserveAspectRatio="xMidYMid meet" aria-hidden="true">
            <g fill="none" stroke="currentColor" stroke-width="3">
              <circle class="flare-dot" cx="12.5" cy="12.5" r="9"/><circle class="flare-dot" cx="37.5" cy="12.5" r="9"/><circle class="flare-dot" cx="62.5" cy="12.5" r="9"/><circle class="flare-dot" cx="87.5" cy="12.5" r="9"/>
              <circle class="flare-dot" cx="12.5" cy="37.5" r="9"/><circle class="flare-dot" cx="37.5" cy="37.5" r="9"/><circle class="flare-dot" cx="62.5" cy="37.5" r="9"/><circle class="flare-dot" cx="87.5" cy="37.5" r="9"/>
              <circle class="flare-dot" cx="12.5" cy="62.5" r="9"/><circle class="flare-dot" cx="37.5" cy="62.5" r="9"/><circle class="flare-dot" cx="62.5" cy="62.5" r="9"/><circle class="flare-dot" cx="87.5" cy="62.5" r="9"/>
              <circle class="flare-dot" cx="12.5" cy="87.5" r="9"/><circle class="flare-dot" cx="37.5" cy="87.5" r="9"/><circle class="flare-dot" cx="62.5" cy="87.5" r="9"/><circle class="flare-dot" cx="87.5" cy="87.5" r="9"/>
            </g>
          </svg>
        </div>
      </div>
      <div class="cm-sep"></div>
      <div class="cm-title cm-jammer-title" id="cm-jammer-title"><span class="cm-label">EW Jammer</span></div>
      <div class="cm-big" id="cm-jammer-val">&mdash;</div>
      <div class="cm-jammer-bar"><div class="cm-bar"><div class="cm-bar-fill" id="cm-bar-fill"></div></div></div>
    </div>
  </div>
<script>
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

// ── State ──────────────────────────────────────────────────────────────────────────
// wpnData.items is already the shell-sliced page (<= 4 weapons); this page never paginates.
// slotYs[i] is the pane-local vertical centre for weapon slot i, fill order L1, L2, R1, R2.
// side(i) = 'left' for i < 2, else 'right'. cmBand = {top, height} of the first key band.
let wpnData = { items: [], selWeapon: null };
let cmData  = { flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 };
let slotYs  = null;
let cmBand  = null;
let wpnNamesKey = '';
let wpnItemEls  = [];

function slotSide(i) { return i < 2 ? 'left' : 'right'; }
// Fallback positions used until the shell forwards real geometry. The weapon keys flanking a
// pane (skipping the top band) sit at ~1/2 and ~5/6 of pane height; slot order L1, L2, R1, R2.
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
  }
}

// ── Weapon list renderer ─────────────────────────────────────────────────────────────
function renderWpn() {
  const list = wpnData.items || [];
  wpnPanel.classList.toggle('has-loadout', list.length > 0);

  const key = list.map(function(w) { return w.n; }).join('|');
  if (key !== wpnNamesKey) {
    wpnNamesKey = key;
    wpnItemEls = [];
    wpnPanel.querySelectorAll('.wp-item').forEach(function(el) { el.remove(); });
    list.forEach(function(w, i) {
      const item = document.createElement('div');
      item.className = 'wp-item ' + slotSide(i);
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
    el.item.style.top = slotY(i) + 'px';
    el.ammo.innerHTML = (w.f > 0) ? ('<span>' + w.a + '</span> / ' + w.f) : '';
    el.item.classList.toggle('sel',   w.n === wpnData.selWeapon);
    el.item.classList.toggle('empty', w.f > 0 && w.a === 0);
  }
}

function repositionRows() {
  for (let i = 0; i < wpnItemEls.length; i++) wpnItemEls[i].item.style.top = slotY(i) + 'px';
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
}

// ── Shell → pane forwarding ────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'wpn') {
    wpnData = { items: Array.isArray(m.items) ? m.items : [], selWeapon: m.selWeapon || null };
    renderWpn();
  } else if (m.type === 'wpn-layout') {
    slotYs = Array.isArray(m.slotYs) ? m.slotYs : null;
    cmBand = (typeof m.cmTop === 'number') ? { top: m.cmTop, height: m.cmHeight } : null;
    applyCmBand();
    repositionRows();
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

window.addEventListener('resize', repositionRows);

renderWpn();   // initial empty-state paint
renderCm();
</script>
</body>
</html>
""";
    }
}
