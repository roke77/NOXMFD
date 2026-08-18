// RC page — Missile Camera: Remote Control feed + controls. See rc.html for the full message
// contract (shell -> page: 'rc' status block + high-rate 'rcaim' reticle; page -> game: rc.*
// commands via sendCommand, src/plugin/CommandDispatcher.cs / RcBridge.cs).
//
// Unlike TGP (pure reactive renderer) this page also OWNS input: a drag-to-aim pointer surface
// over the feed, plus a few buttons. Aim deltas are batched on a short timer rather than sent per
// pointermove — same reasoning as the physical mouse (PollMouse accumulates a frame's motion,
// not each OS mouse-move event), and it keeps this to a handful of POSTs/sec instead of hundreds.

const rcPanel   = document.getElementById('rc-panel');
const rcEmptyMsg= document.getElementById('rc-empty-msg');
const rcSurface = document.getElementById('rc-surface');
const rcImg     = document.getElementById('rc-img');
const rcReticle = document.getElementById('rc-reticle');
const rcMissile = document.getElementById('rc-missile');
const rcLink    = document.getElementById('rc-link');
const rcFormation = document.getElementById('rc-formation');
const rcThrFill = document.getElementById('rc-thr-fill');
const rcBoostBtn= document.getElementById('rc-boost');
const rcTakeBtn = document.getElementById('rc-take');
const rcReleaseBtn = document.getElementById('rc-release');
const rcFormBtn = document.getElementById('rc-form-btn');
const rcVisionBtn = document.getElementById('rc-vision');
const rcDetBtn  = document.getElementById('rc-det');
const rcThrUp   = document.getElementById('rc-thr-up');
const rcThrDown = document.getElementById('rc-thr-down');
const rcPoolEl  = document.getElementById('rc-pool');
const rcTeleSpd = document.getElementById('rc-tele-spd');
const rcTeleAlt = document.getElementById('rc-tele-alt');
const rcTeleRng = document.getElementById('rc-tele-rng');
const rcTeleFuel = document.getElementById('rc-tele-fuel');
const rcTeleMach = document.getElementById('rc-tele-mach');
const rcTeleG   = document.getElementById('rc-tele-g');
const rcTeleGuid = document.getElementById('rc-tele-guid');
const rcTeleTgtAngle = document.getElementById('rc-tele-tgtangle');
const rcTeleTti = document.getElementById('rc-tele-tti');
const rcMarkersEl = document.getElementById('rc-markers');

// Last known 'rc' status block, so button handlers (which fire outside the message handler)
// can read current state without their own bookkeeping.
let state = { available: false, fsActive: false, controlling: false, formation: false, pool: [] };

rcImg.src = '/rc.mjpg';
rcImg.addEventListener('error', function() { rcPanel.classList.remove('has-feed'); });

// ── Aim drag ────────────────────────────────────────────────────────────────────────
// Degrees per CSS pixel of drag — tuned to feel roughly like the in-cockpit mouse sensitivity
// at default game settings; the RC bind is itself a slew rate, not a fixed mapping, so this is
// a starting point rather than a precise conversion. Adjust here if it feels too twitchy/slow.
const AIM_DEG_PER_PX = 0.15;
const AIM_SEND_MS = 40;   // ~25 Hz — smooth without flooding /command

let dragging = false;
let lastX = 0, lastY = 0;
let pendingYaw = 0, pendingPitch = 0;

rcSurface.addEventListener('pointerdown', function(e) {
  if (!state.controlling) return;
  dragging = true;
  lastX = e.clientX;
  lastY = e.clientY;
  rcSurface.setPointerCapture(e.pointerId);
});

rcSurface.addEventListener('pointermove', function(e) {
  if (!dragging) return;
  const dx = e.clientX - lastX;
  const dy = e.clientY - lastY;
  lastX = e.clientX;
  lastY = e.clientY;
  pendingYaw   += dx * AIM_DEG_PER_PX;
  pendingPitch += dy * AIM_DEG_PER_PX;   // screen-down == pitch-up-negative, matches RcBridge's convention
});

function endDrag(e) {
  if (!dragging) return;
  dragging = false;
  try { rcSurface.releasePointerCapture(e.pointerId); } catch (err) {}
}
rcSurface.addEventListener('pointerup', endDrag);
rcSurface.addEventListener('pointercancel', endDrag);
rcSurface.addEventListener('pointerleave', endDrag);

setInterval(function() {
  if (pendingYaw === 0 && pendingPitch === 0) return;
  sendCommand('rc.aim', { x: pendingYaw, y: pendingPitch }).catch(function() {});
  pendingYaw = 0;
  pendingPitch = 0;
}, AIM_SEND_MS);

// ── Buttons ─────────────────────────────────────────────────────────────────────────
rcTakeBtn.addEventListener('click', function() {
  sendCommand('rc.take', {}).catch(function() {});
});
rcReleaseBtn.addEventListener('click', function() {
  sendCommand('rc.release', {}).catch(function() {});
});
rcFormBtn.addEventListener('click', function() {
  sendCommand('rc.formation', {}).catch(function() {});
});
rcVisionBtn.addEventListener('click', function() {
  sendCommand('rc.vision-cycle', {}).catch(function() {});
});
rcThrUp.addEventListener('click', function() {
  sendCommand('rc.throttle-adjust', { v: 0.1 }).catch(function() {});
});
rcThrDown.addEventListener('click', function() {
  sendCommand('rc.throttle-adjust', { v: -0.1 }).catch(function() {});
});

// Afterburner is a hold, not a tap — mirrors the physical keybind's level-triggered behavior
// (ThrottleController.SetExternalBoost). Pointer events cover mouse + touch uniformly.
rcBoostBtn.addEventListener('pointerdown', function() {
  sendCommand('rc.boost', { on: true }).catch(function() {});
});
function releaseBoost() {
  sendCommand('rc.boost', { on: false }).catch(function() {});
}
rcBoostBtn.addEventListener('pointerup', releaseBoost);
rcBoostBtn.addEventListener('pointercancel', releaseBoost);
rcBoostBtn.addEventListener('pointerleave', releaseBoost);

// Manual detonate is irreversible — require a deliberate ~600 ms press-and-hold rather than a
// single tap, the same way a cockpit guard flap makes a critical action hard to fat-finger.
const DETONATE_HOLD_MS = 600;
let detonateTimer = null;
rcDetBtn.addEventListener('pointerdown', function() {
  rcDetBtn.classList.add('on');
  detonateTimer = setTimeout(function() {
    detonateTimer = null;
    sendCommand('rc.detonate', {}).catch(function() {});
  }, DETONATE_HOLD_MS);
});
function cancelDetonate() {
  rcDetBtn.classList.remove('on');
  if (detonateTimer) { clearTimeout(detonateTimer); detonateTimer = null; }
}
rcDetBtn.addEventListener('pointerup', cancelDetonate);
rcDetBtn.addEventListener('pointercancel', cancelDetonate);
rcDetBtn.addEventListener('pointerleave', cancelDetonate);

// ── Missile picker ──────────────────────────────────────────────────────────────────
// Only worth showing while nothing is under control — once controlling, TAKE targets "next
// best" and the picker would just be a second, redundant way to do the same thing.
function renderPool(pool) {
  rcPoolEl.innerHTML = '';
  if (state.controlling || !pool || pool.length === 0) return;
  pool.forEach(function(name, i) {
    const item = document.createElement('div');
    item.className = 'rc-pool-item';
    item.textContent = name || ('#' + i);
    item.addEventListener('click', function() {
      sendCommand('rc.take-at', { index: i }).catch(function() {});
    });
    rcPoolEl.appendChild(item);
  });
}

// ── Telemetry readout ───────────────────────────────────────────────────────────────
// Mirrors the base MissileCamera mod's own Fullscreen HUD text 1:1 — these are already
// formatted (units, rounding) server-side, so this just places them, no reformatting.
function renderTele(tele) {
  const has = !!tele;
  rcPanel.classList.toggle('has-tele', has);

  // visionMode rides even in the "no trackable missile" partial object (see McBridge.cs
  // TelemetryJson) — it's a global selection, not missile-specific — so update the button
  // regardless of `has`, and bail only for the fields that genuinely need a missile.
  rcVisionBtn.textContent = (has && tele.visionMode) ? tele.visionMode.replace(/^MODE:\s*/, 'VIS ') : 'VIS';
  if (!has) return;

  rcTeleSpd.textContent = tele.speed || '';
  rcTeleAlt.textContent = tele.alt || '';
  rcTeleRng.textContent = tele.range || '';
  rcTeleFuel.textContent = tele.fuel || '';
  rcTeleMach.textContent = tele.mach || '';
  rcTeleG.textContent = tele.g || '';
  rcTeleGuid.textContent = tele.guidance || '';
  rcTeleTgtAngle.textContent = tele.hasTarget ? (tele.tgtAngle || '') : '';

  if (tele.hasTti) {
    rcTeleTti.textContent = 'TTI ' + tele.ttiSec.toFixed(1) + 's';
  } else {
    rcTeleTti.textContent = '';
  }
}

// ── Target markers ──────────────────────────────────────────────────────────────────
// Cockpit HUD markers reprojected onto the feed camera (docs/rc-page.md "rc.markers") — same
// viewport-flip reasoning as the aim reticle (server uses Unity's y=0-bottom convention).
// Full clear+rebuild each update: marker counts are small (a handful of contacts), and this
// rides the normal telemetry rate, not the high-rate rcaim channel — no need to diff.
function renderMarkers(markers) {
  rcMarkersEl.innerHTML = '';
  if (!markers || markers.length === 0) return;

  markers.forEach(function(m) {
    const el = document.createElement('div');
    el.className = 'rc-marker' + (m.sel ? ' selected' : '');
    el.style.left = (m.x * 100) + '%';
    el.style.top = ((1 - m.y) * 100) + '%';
    el.style.color = m.c || '#ffffff';

    if (m.n) {
      const label = document.createElement('div');
      label.className = 'rc-marker-label';
      label.textContent = m.n;
      el.appendChild(label);
    }

    rcMarkersEl.appendChild(el);
  });
}

// ── Status rendering ────────────────────────────────────────────────────────────────
function applyRcState(m) {
  state = m;

  rcPanel.classList.toggle('has-rc', !!m.available);
  rcPanel.classList.toggle('has-feed', !!m.fsActive);

  if (!m.available) {
    rcEmptyMsg.textContent = '— NOT INSTALLED —';
  } else if (!m.fsActive) {
    rcEmptyMsg.textContent = '— CAMERA NOT ACTIVE —';
  }

  rcMissile.textContent = m.controlling ? (m.missile || '') : '';

  rcLink.textContent = m.controlling ? (m.link || '') : '';
  rcLink.classList.toggle('degraded', m.link === 'Degraded');
  rcLink.classList.toggle('lost', m.link === 'Lost');

  rcFormation.classList.toggle('active', !!m.formation);

  rcThrFill.style.height = Math.round((m.thr || 0) * 100) + '%';
  rcBoostBtn.classList.toggle('on', !!m.boost);

  rcTakeBtn.disabled = !m.fsActive || m.controlling;
  rcReleaseBtn.disabled = !m.controlling;
  rcFormBtn.disabled = !m.controlling;
  rcDetBtn.disabled = !m.controlling;

  renderPool(m.pool);
  renderTele(m.tele);
  renderMarkers(m.markers);
}

// ── Shell messages ──────────────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;

  if (m.type === 'rc') {
    applyRcState(m);
  } else if (m.type === 'rcaim') {
    // RcBridge.ReticleViewport uses Unity's viewport convention (0 = bottom, 1 = top — same as
    // Camera.WorldToViewportPoint), but CSS `top` counts from the top of the box. Flip Y here;
    // X needs no flip (both conventions agree left→right).
    rcReticle.style.left = (m.x * 100) + '%';
    rcReticle.style.top  = ((1 - m.y) * 100) + '%';
  } else if (m.type === 'orient') {
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
  }
});
