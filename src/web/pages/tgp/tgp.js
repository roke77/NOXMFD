// TGP page — targeting-pod MJPEG feed. A pure reactive renderer driven by the shell over
// postMessage; single source of truth for BOTH layouts (full-screen iframe + split pane).
// See tgp.html for the message contract.

const tgpPanel = document.getElementById('tgp-panel');
const tgpImg   = document.getElementById('tgp-img');

// Start the MJPEG connection immediately; the server only emits frames while a target is
// locked, so the img stays hidden (NO LOCK shown) until the shell forwards active:true.
// MJPEG fires 'load' only once, so it can't detect a stall — but 'error' still covers the
// hard case where the connection breaks outright (network blip, backgrounded tab, server-side
// disconnect). <img> never retries a stream on its own, so without this the feed stays dead
// until the whole page is reloaded — reopen the connection ourselves instead.
let tgpRetryCount = 0;
let tgpRetryTimer = null;
function scheduleTgpRetry() {
  if (tgpRetryTimer) return;
  tgpRetryTimer = setTimeout(function () {
    tgpRetryTimer = null;
    tgpImg.src = '/tgp.mjpg?r=' + (++tgpRetryCount);
  }, 1200);
}
tgpImg.src = '/tgp.mjpg';
tgpImg.addEventListener('error', function() {
  tgpPanel.classList.remove('has-feed');
  scheduleTgpRetry();
});

// HQ-mode stat overlay (docs/tgp-high-quality-mode.md) — drawn from the shell's 'tgp' message.
// Native mode already has this baked into the video (the game's own stacked-camera UICam), so it
// only shows when quality is "hq" AND a lock is active. Layout matches the in-cockpit
// TargetScreenUI screen (stacked corner groups + a bearing compass + a lock box per target), not
// the raw field order.
const ovType    = document.getElementById('tgp-ov-type');
const ovPilot   = document.getElementById('tgp-ov-pilot');
const ovRng     = document.getElementById('tgp-ov-rng');
const ovAlt     = document.getElementById('tgp-ov-alt');
const ovSpd     = document.getElementById('tgp-ov-spd');
const ovHdg     = document.getElementById('tgp-ov-hdg');
const ovRelAlt  = document.getElementById('tgp-ov-relalt');
const ovRelSpd  = document.getElementById('tgp-ov-relspd');
const ovNeedle  = document.getElementById('tgp-ov-needle');
const ovBrg     = document.getElementById('tgp-ov-bearing');
const ovGrid    = document.getElementById('tgp-ov-grid');
const ovMode    = document.getElementById('tgp-ov-mode');
const ovMag     = document.getElementById('tgp-ov-mag');
const ovBoxes   = document.getElementById('tgp-ov-boxes');

const TGP_STATUS_TAG = { jammed: 'JAM', lased: 'LASE', outdated: 'OLD' };

// Raw meters/mps — not yet converted to the player's UnitConverter preference (km/nm, ft/m,
// kt/mps); a known simplification, not a silent bug. Revisit if this needs to match the
// in-cockpit readout's units exactly.
function fmtDash(value, suffix) { return value == null ? '-' : Math.round(value) + suffix; }

function applyOverlay(quality, data) {
  const show = quality === 'hq' && !!data && data.cnt > 0;
  tgpPanel.classList.toggle('show-overlay', show);
  if (!show) { renderBoxes([]); return; }

  ovType.textContent = data.type;
  ovType.className   = 'tgp-ov-title' + (data.status === 'friendly' ? ' friendly' : ' hostile');
  const tag = TGP_STATUS_TAG[data.status];
  if (tag) {
    const span = document.createElement('span');
    span.className = 'tgp-ov-tag';
    span.textContent = '[' + tag + ']';
    ovType.appendChild(span);
  }
  ovPilot.textContent = data.pilot || '';

  ovRng.textContent = 'RNG ' + (data.range / 1000).toFixed(1) + 'km';

  if (data.hasDetail) {
    ovAlt.textContent    = 'ALT ' + fmtDash(data.alt, 'm');
    ovSpd.textContent    = 'SPD ' + fmtDash(data.spd, 'm/s');
    ovHdg.textContent    = 'HDG ' + fmtDash(data.hdg, '°');
    ovRelAlt.textContent = 'REL ' + fmtDash(data.relAlt, 'm');
    ovRelSpd.textContent = 'REL ' + fmtDash(data.relSpd, 'm/s');
  } else {
    ovAlt.textContent = 'ALT -'; ovSpd.textContent = 'SPD -'; ovHdg.textContent = 'HDG -';
    ovRelAlt.textContent = 'REL -'; ovRelSpd.textContent = 'REL -';
  }

  ovNeedle.style.transform = 'rotate(' + data.brg + 'deg)';
  ovBrg.textContent = Math.round(data.brg) + '°';
  ovGrid.textContent = 'GRID: ' + data.grid;
  ovMode.textContent = 'MODE: ' + (data.ir ? 'IR' : 'COLOR');
  ovMag.textContent  = 'Mag x' + data.mag.toFixed(1);

  renderBoxes(data.boxes);
}

// One <div> per locked target, positioned from the server's WorldToViewportPoint output
// (0-1, y-up — flipped here since CSS top is top-down). Rebuilt each update; box counts are
// small (typically 1-3 targets) so this is simpler than diffing/pooling elements.
function renderBoxes(boxes) {
  ovBoxes.replaceChildren();
  if (!Array.isArray(boxes)) return;
  for (const b of boxes) {
    if (!b.vis || b.x < 0 || b.x > 1 || b.y < 0 || b.y > 1) continue;
    const div = document.createElement('div');
    div.className = 'tgp-ov-box ' + (b.status || 'target');
    div.style.left = (b.x * 100) + '%';
    div.style.top  = ((1 - b.y) * 100) + '%';
    ovBoxes.appendChild(div);
  }
}

window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgp') {
    tgpPanel.classList.toggle('has-feed', !!m.active);
    applyOverlay(m.quality || 'native', m.data || null);
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell — drives body.portrait/.landscape so any
    // orientation rules track the device, not the (wide+short) pane box.
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
