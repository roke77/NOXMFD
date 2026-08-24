// Reads /rates-config once on load for the slider's starting position. Label updates live on
// 'input'; rates.set only fires on 'change' — a ConfigEntry.Value write does a synchronous .cfg
// save, too costly to run on every drag tick.

if (window.parent !== window) {
  var back = document.querySelector('.mcfg-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('mcfg-panel');

var DEFAULT_FAST_HZ = 10;   // matches RatesConfig.cs Bind() default

function setSlider(hz) {
  var slider = document.getElementById('mcfg-tlm-slider');
  var val    = document.getElementById('mcfg-tlm-val');
  slider.value = hz;
  val.textContent = hz + ' Hz';
}

document.getElementById('mcfg-tlm-slider').oninput = function () {
  document.getElementById('mcfg-tlm-val').textContent = this.value + ' Hz';
};
document.getElementById('mcfg-tlm-slider').onchange = function () {
  sendCommand('rates.set', { group: 'fast', hz: Number(this.value) }).catch(function () {});
};

document.getElementById('mcfg-reset').onclick = function () {
  setSlider(DEFAULT_FAST_HZ);
  sendCommand('rates.set', { group: 'fast', hz: DEFAULT_FAST_HZ }).catch(function () {});
};

fetch('/rates-config')
  .then(function (r) { return r.json(); })
  .then(function (cfg) {
    setSlider(cfg.fastHz);
    panelEl.classList.remove('unavailable');
  })
  .catch(function () { panelEl.classList.add('unavailable'); });
