// Reads /rates-config once on load for slider starting positions. Label updates live on 'input';
// rates.set only fires on 'change' — a ConfigEntry.Value write does a synchronous .cfg save, too
// costly to run on every drag tick.

if (window.parent !== window) {
  var back = document.querySelector('.rts-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('rts-panel');

var DEFAULTS = { fastHz: 10, tgpHz: 15, tgpQuality: 'native' };   // matches RatesConfig.cs Bind() defaults

function setSlider(sliderId, valId, hz) {
  var slider = document.getElementById(sliderId);
  var val    = document.getElementById(valId);
  slider.value = hz;
  val.textContent = hz + ' Hz';
}

// Measured (docs/performance.md, cfg-rates branch): 15 Hz is the shipped-safe default; 30 Hz drops
// up to ~45% of captures with 50ms+ frame spikes. No fixed cap — the player can still choose a
// higher rate if they have the GPU headroom — but they should see the cost before picking it.
var TGP_HZ_WARNING_ABOVE = 15;

function updateTgpWarning(hz) {
  var warning = document.getElementById('rts-tgp-hz-warning');
  warning.classList.toggle('shown', hz > TGP_HZ_WARNING_ABOVE);
}

function wireSlider(sliderId, valId, group) {
  var slider = document.getElementById(sliderId);
  slider.oninput = function () {
    var hz = Number(slider.value);
    document.getElementById(valId).textContent = hz + ' Hz';
    if (group === 'tgp') updateTgpWarning(hz);
  };
  slider.onchange = function () {
    sendCommand('rates.set', { group: group, hz: Number(slider.value) }).catch(function () {});
  };
}
wireSlider('rts-tlm-slider', 'rts-tlm-val', 'fast');
wireSlider('rts-tgp-slider', 'rts-tgp-val', 'tgp');

function setQuality(quality) {
  var buttons = document.querySelectorAll('#rts-tgp-quality-row .rts-quality-btn');
  buttons.forEach(function (btn) {
    btn.classList.toggle('active', btn.dataset.quality === quality);
  });
  document.getElementById('rts-tgp-quality-warning').classList.toggle('shown', quality !== 'native');
}

document.querySelectorAll('#rts-tgp-quality-row .rts-quality-btn').forEach(function (btn) {
  btn.onclick = function () {
    var quality = btn.dataset.quality;
    setQuality(quality);
    sendCommand('rates.set', { group: 'tgpQuality', wname: quality }).catch(function () {});
  };
});

document.getElementById('rts-reset').onclick = function () {
  setSlider('rts-tlm-slider', 'rts-tlm-val', DEFAULTS.fastHz);
  setSlider('rts-tgp-slider', 'rts-tgp-val', DEFAULTS.tgpHz);
  updateTgpWarning(DEFAULTS.tgpHz);
  setQuality(DEFAULTS.tgpQuality);
  sendCommand('rates.set', { group: 'fast', hz: DEFAULTS.fastHz }).catch(function () {});
  sendCommand('rates.set', { group: 'tgp',  hz: DEFAULTS.tgpHz  }).catch(function () {});
  sendCommand('rates.set', { group: 'tgpQuality', wname: DEFAULTS.tgpQuality }).catch(function () {});
};

function applyConfig(cfg) {
  setSlider('rts-tlm-slider', 'rts-tlm-val', cfg.fastHz);
  setSlider('rts-tgp-slider', 'rts-tgp-val', cfg.tgpHz);
  updateTgpWarning(cfg.tgpHz);
  setQuality(cfg.tgpQuality || 'native');
  panelEl.classList.remove('unavailable');
}

fetch('/rates-config')
  .then(function (r) { return r.json(); })
  .then(applyConfig)
  .catch(function () { panelEl.classList.add('unavailable'); });
