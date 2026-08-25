// TGP page — targeting-pod MJPEG feed. A pure reactive renderer driven by the shell over
// postMessage; single source of truth for BOTH layouts (full-screen iframe + split pane).
// See tgp.html for the message contract.

const tgpPanel   = document.getElementById('tgp-panel');
const tgpImg     = document.getElementById('tgp-img');
const tgpOverlay = document.getElementById('tgp-overlay');

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

// Keeps the overlay's box pinned to the <img>'s real letterboxed content rect, not the panel's
// own box — object-fit:contain centers the picture inside whatever box the panel ends up with,
// and that box's aspect only matches the frame's 3:2 when the container's own shape allows it
// (see tgp.css's .tgp-overlay comment). 'load' only fires once per MJPEG connection, but that's
// enough to learn naturalWidth/naturalHeight since the capture size doesn't change frame to frame;
// ResizeObserver re-derives the rect whenever the panel itself changes shape (split-pane resize,
// orientation change, browser resize).
function syncOverlayRect() {
  const nw = tgpImg.naturalWidth, nh = tgpImg.naturalHeight;
  if (!nw || !nh) return;
  const pw = tgpPanel.clientWidth, ph = tgpPanel.clientHeight;
  if (!pw || !ph) return;
  const panelAspect = pw / ph, imgAspect = nw / nh;
  let w, h;
  if (imgAspect > panelAspect) { w = pw; h = w / imgAspect; }
  else                          { h = ph; w = h * imgAspect; }
  tgpOverlay.style.left   = ((pw - w) / 2) + 'px';
  tgpOverlay.style.top    = ((ph - h) / 2) + 'px';
  tgpOverlay.style.width  = w + 'px';
  tgpOverlay.style.height = h + 'px';
}
tgpImg.addEventListener('load', syncOverlayRect);
new ResizeObserver(syncOverlayRect).observe(tgpPanel);

// HQ-mode stat overlay (docs/tgp-high-quality-mode.md) — drawn from the shell's 'tgp' message.
// Native mode already has this baked into the video for free, because Native captures the game's
// own stacked-camera UICam output directly — true for a locked target AND for manual mode
// (TgpNativeOverlay populates the exact same TargetScreenUI fields either way, docs/tgp-manual-
// control.md's "In-cockpit overlay"). So this only ever draws client-side in HQ quality, for
// EITHER case — drawing it in Native too double-shows everything (baked into the pixels AND drawn
// again as HTML on top), which is exactly what happened before this comment was corrected. Layout
// matches the in-cockpit TargetScreenUI screen (stacked corner groups + a bearing compass + a lock
// box per target), not the raw field order.
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
  const manual = !!data && !!data.manual;
  const locked = !!data && data.cnt > 0;
  const show = quality === 'hq' && (manual || locked);
  tgpPanel.classList.toggle('show-overlay', show);
  tgpPanel.classList.toggle('tgp-point-track', manual && !!data.pointTrack);
  if (!show) { renderBoxes([]); return; }

  // Resync every update rather than trusting 'load'/ResizeObserver alone — on first lock,
  // ResizeObserver's initial callback can fire before the MJPEG <img> has a naturalWidth yet, and
  // syncOverlayRect() silently no-ops until something un-stale triggers it again (a manual resize
  // is what synced it before). This makes it correct on the very first frame, at negligible cost.
  syncOverlayRect();

  if (manual) { applyManualOverlay(data); return; }

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
  ovSpd.classList.remove('tgp-ov-hidden');

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

  // +180: camMount's local yaw isn't nose-relative the way wpt.js's relativeBearing is — it's 180°
  // off that, so a plain rotate(brg) (matching wpt.js's compass convention) lands exactly opposite
  // the real needle across four confirmed clock positions (9/10/11/12 real -> 3/4/5/6 shown, a
  // clean point reflection, not a mirror). Confirmed via precise in-game clock-position reads, not
  // screenshot pixel-reading — the latter isn't reliable enough for a calibration this exact.
  ovNeedle.style.transform = 'rotate(' + (data.brg + 180) + 'deg)';
  ovBrg.textContent = Math.round(data.brg) + '°';
  ovGrid.textContent = 'GRID: ' + data.grid;
  ovMode.textContent = 'MODE: ' + (data.ir ? 'IR' : 'COLOR');
  ovMag.textContent  = 'Mag x' + data.mag.toFixed(1);

  renderBoxes(data.boxes);
}

// Manual mode's own field mapping — same server-computed values TgpNativeOverlay draws onto the
// in-cockpit screen (TgpManualControl.ComputeOverlaySample), just rendered into this page's
// existing corner-group elements instead of duplicating that layout. No pilot (never a real
// target), no per-target lock boxes, and no own-aircraft SPD (duplicates the flight HUD — same
// call TgpNativeOverlay made). HDG's slot carries elevation instead, matching the in-cockpit
// overlay's own repurposing (a locked target only ever needed bearing; manual pointing has both).
// CLO (closure rate) arrives pre-formatted (data.clo, via UnitConverter.SpeedReading server-side)
// rather than a raw m/s number — closure is new to this page, so unlike RNG/ALT/etc. above (which
// keep the page's existing raw-units simplification, see fmtDash's own comment) there was no
// reason to introduce a fresh native/web unit mismatch for it.
function applyManualOverlay(data) {
  ovType.textContent = data.pointTrack ? 'POINT TRACK' : 'MANUAL';
  ovType.className   = 'tgp-ov-title';
  ovPilot.textContent = '';
  ovSpd.classList.add('tgp-ov-hidden');

  ovRng.textContent = data.hasDetail ? 'RNG ' + (data.range / 1000).toFixed(1) + 'km' : 'RNG -';
  ovAlt.textContent    = data.hasDetail ? 'ALT ' + fmtDash(data.alt, 'm')    : 'ALT -';
  ovRelAlt.textContent = data.hasDetail ? 'REL ' + fmtDash(data.relAlt, 'm') : 'REL -';
  ovRelSpd.textContent = 'CLO ' + (data.clo || '-');
  ovHdg.textContent    = 'EL ' + Math.round(data.el) + '°';

  ovNeedle.style.transform = 'rotate(' + (data.brg + 180) + 'deg)';
  ovBrg.textContent = Math.round(data.brg) + '°';
  ovGrid.textContent = data.hasDetail ? 'GRID: ' + data.grid : 'GRID: -';
  ovMode.textContent = 'MODE: ' + (data.ir ? 'IR' : 'COLOR');
  ovMag.textContent  = 'Mag x' + data.mag.toFixed(1);

  renderBoxes([]);
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
    tgpPanel.classList.toggle('tgp-manual', !!m.manual);
    applyOverlay(m.quality || 'native', m.data || null);
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell — drives body.portrait/.landscape so any
    // orientation rules track the device, not the (wide+short) pane box.
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
