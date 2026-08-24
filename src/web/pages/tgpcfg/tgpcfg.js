// Reads /rates-config once on load for slider/quality starting state. Label updates live on
// 'input'; rates.set only fires on 'change' — a ConfigEntry.Value write does a synchronous .cfg
// save, too costly to run on every drag tick.

if (window.parent !== window) {
  var back = document.querySelector('.tcfg-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('tcfg-panel');

var DEFAULTS = { tgpHz: 15, tgpQuality: 'native', tgpSuppressNative: false };   // matches RatesConfig.cs Bind() defaults
var tgpQuality = DEFAULTS.tgpQuality;
var tgpSuppressNative = DEFAULTS.tgpSuppressNative;

function setSlider(hz) {
  var slider = document.getElementById('tcfg-tgp-slider');
  var val    = document.getElementById('tcfg-tgp-val');
  slider.value = hz;
  val.textContent = hz + ' Hz';
}

// Measured (docs/performance.md, cfg-rates branch): 15 Hz is the shipped-safe default; 30 Hz drops
// up to ~45% of captures with 50ms+ frame spikes. No fixed cap — the player can still choose a
// higher rate if they have the GPU headroom — but they should see the cost before picking it.
var TGP_HZ_WARNING_ABOVE = 15;

function updateTgpWarning(hz) {
  document.getElementById('tcfg-tgp-hz-warning').classList.toggle('shown', hz > TGP_HZ_WARNING_ABOVE);
}

document.getElementById('tcfg-tgp-slider').oninput = function () {
  var hz = Number(this.value);
  document.getElementById('tcfg-tgp-val').textContent = hz + ' Hz';
  updateTgpWarning(hz);
};
document.getElementById('tcfg-tgp-slider').onchange = function () {
  sendCommand('rates.set', { group: 'tgp', hz: Number(this.value) }).catch(function () {});
};

function setQuality(quality) {
  tgpQuality = quality;
  var buttons = document.querySelectorAll('#tcfg-tgp-quality-row .tcfg-quality-btn');
  buttons.forEach(function (btn) {
    btn.classList.toggle('active', btn.dataset.quality === quality);
  });
  document.getElementById('tcfg-tgp-quality-warning').classList.toggle('shown', quality !== 'native');
  renderSuppressToggle();
}

document.querySelectorAll('#tcfg-tgp-quality-row .tcfg-quality-btn').forEach(function (btn) {
  btn.onclick = function () {
    var quality = btn.dataset.quality;
    setQuality(quality);
    sendCommand('rates.set', { group: 'tgpQuality', wname: quality }).catch(function () {});
  };
});

function setSuppressNative(on) {
  tgpSuppressNative = !!on;
  renderSuppressToggle();
}

function renderSuppressToggle() {
  var row = document.getElementById('tcfg-tgp-suppress');
  var btn = document.getElementById('tcfg-tgp-suppress-btn');
  row.hidden = false;
  row.classList.remove('disabled');
  btn.disabled = false;
  btn.textContent = tgpSuppressNative ? 'ON' : 'OFF';
  btn.classList.toggle('on', tgpSuppressNative);
}

document.getElementById('tcfg-tgp-suppress-btn').onclick = function () {
  setSuppressNative(!tgpSuppressNative);
  sendCommand('rates.set', {
    group: 'tgpSuppressNative',
    wname: tgpSuppressNative ? 'on' : 'off',
    on: tgpSuppressNative
  }).catch(function () {});
};

document.getElementById('tcfg-reset').onclick = function () {
  setSlider(DEFAULTS.tgpHz);
  updateTgpWarning(DEFAULTS.tgpHz);
  setQuality(DEFAULTS.tgpQuality);
  setSuppressNative(DEFAULTS.tgpSuppressNative);
  sendCommand('rates.set', { group: 'tgp', hz: DEFAULTS.tgpHz }).catch(function () {});
  sendCommand('rates.set', { group: 'tgpQuality', wname: DEFAULTS.tgpQuality }).catch(function () {});
  sendCommand('rates.set', { group: 'tgpSuppressNative', wname: 'off', on: false }).catch(function () {});
};

fetch('/rates-config')
  .then(function (r) { return r.json(); })
  .then(function (cfg) {
    setSlider(cfg.tgpHz);
    updateTgpWarning(cfg.tgpHz);
    setQuality(cfg.tgpQuality || 'native');
    setSuppressNative(!!cfg.tgpSuppressNative);
    panelEl.classList.remove('unavailable');
  })
  .catch(function () { panelEl.classList.add('unavailable'); });

renderSuppressToggle();
