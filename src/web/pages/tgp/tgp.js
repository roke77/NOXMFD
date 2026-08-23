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
// only shows when quality is "hq" AND a lock is active. Target lock box deliberately deferred to
// its own branch — see tgp.html's comment.
const ovMag    = document.getElementById('tgp-ov-mag');
const ovMode   = document.getElementById('tgp-ov-mode');
const ovGrid   = document.getElementById('tgp-ov-grid');
const ovBrg    = document.getElementById('tgp-ov-bearing');
const ovType   = document.getElementById('tgp-ov-type');
const ovPilot  = document.getElementById('tgp-ov-pilot');
const ovHdg    = document.getElementById('tgp-ov-hdg');
const ovAlt    = document.getElementById('tgp-ov-alt');
const ovRelAlt = document.getElementById('tgp-ov-relalt');
const ovSpd    = document.getElementById('tgp-ov-spd');
const ovRelSpd = document.getElementById('tgp-ov-relspd');

const TGP_STATUS_TAG = { jammed: 'JAM', lased: 'LASE', outdated: 'OLD' };

// Raw meters/mps — not yet converted to the player's UnitConverter preference (km/nm, ft/m,
// kt/mps); a known simplification, not a silent bug. Revisit if this needs to match the
// in-cockpit readout's units exactly.
function fmtDash(value, suffix) { return value == null ? '-' : Math.round(value) + suffix; }

function applyOverlay(quality, data) {
  const show = quality === 'hq' && !!data && data.cnt > 0;
  tgpPanel.classList.toggle('show-overlay', show);
  if (!show) return;

  ovMag.textContent  = 'MAG x' + data.mag.toFixed(1);
  ovMode.textContent = 'MODE: ' + (data.ir ? 'IR' : 'COLOR');
  ovGrid.textContent = 'GRID: ' + data.grid;
  ovBrg.textContent  = Math.round(data.brg) + '°';

  ovType.textContent = data.type;
  ovType.className   = 'tgp-ov-type' + (data.status === 'friendly' ? ' friendly' : ' hostile');
  const tag = TGP_STATUS_TAG[data.status];
  if (tag) {
    const span = document.createElement('span');
    span.className = 'tgp-ov-tag';
    span.textContent = '[' + tag + ']';
    ovType.appendChild(span);
  }
  ovPilot.textContent = data.pilot || '';
  ovPilot.style.display = data.pilot ? '' : 'none';

  if (data.hasDetail) {
    ovHdg.textContent    = 'HDG ' + fmtDash(data.hdg, '°');
    ovAlt.textContent    = 'ALT ' + fmtDash(data.alt, 'm');
    ovRelAlt.textContent = 'REL ' + fmtDash(data.relAlt, 'm');
    ovSpd.textContent    = 'SPD ' + fmtDash(data.spd, 'm/s');
    ovRelSpd.textContent = 'REL ' + fmtDash(data.relSpd, 'm/s');
  } else {
    ovHdg.textContent = 'HDG -'; ovAlt.textContent = 'ALT -'; ovRelAlt.textContent = 'REL -';
    ovSpd.textContent = 'SPD -'; ovRelSpd.textContent = 'REL -';
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
