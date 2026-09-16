// Reads /rates-config once on load for the sliders' and toggle's starting position. Slider label
// updates live on 'input'; rates.set only fires on 'change' — a ConfigEntry.Value write does a
// synchronous .cfg save, too costly to run on every drag tick.

if (window.parent !== window) {
  var back = document.querySelector('.mcfg-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('mcfg-panel');

var DEFAULT_FAST_HZ = 10;      // matches RatesConfig.cs Bind() default
var DEFAULT_CONTACT_HZ = 4;    // matches RatesConfig.cs Bind() default
var DEFAULT_SHOW_PLAYER_NAMES = false;   // matches RatesConfig.cs Bind() default

var showPlayerNames = DEFAULT_SHOW_PLAYER_NAMES;

function setSlider(prefix, hz) {
  var slider = document.getElementById('mcfg-' + prefix + '-slider');
  var val    = document.getElementById('mcfg-' + prefix + '-val');
  slider.value = hz;
  val.textContent = hz + ' Hz';
}

document.getElementById('mcfg-tlm-slider').oninput = function () {
  document.getElementById('mcfg-tlm-val').textContent = this.value + ' Hz';
};
document.getElementById('mcfg-tlm-slider').onchange = function () {
  sendCommand('rates.set', { group: 'fast', hz: Number(this.value) }).catch(function () {});
};

document.getElementById('mcfg-contact-slider').oninput = function () {
  document.getElementById('mcfg-contact-val').textContent = this.value + ' Hz';
};
document.getElementById('mcfg-contact-slider').onchange = function () {
  sendCommand('rates.set', { group: 'contact', hz: Number(this.value) }).catch(function () {});
};

function setShowPlayerNames(on) {
  showPlayerNames = !!on;
  var btn = document.getElementById('mcfg-show-player-names-btn');
  btn.textContent = showPlayerNames ? 'ON' : 'OFF';
  btn.classList.toggle('on', showPlayerNames);
}

document.getElementById('mcfg-show-player-names-btn').onclick = function () {
  setShowPlayerNames(!showPlayerNames);
  sendCommand('rates.set', {
    group: 'mapShowPlayerNames',
    wname: showPlayerNames ? 'on' : 'off',
    on: showPlayerNames
  }).catch(function () {});
};

document.getElementById('mcfg-reset').onclick = function () {
  setSlider('tlm', DEFAULT_FAST_HZ);
  setSlider('contact', DEFAULT_CONTACT_HZ);
  setShowPlayerNames(DEFAULT_SHOW_PLAYER_NAMES);
  sendCommand('rates.set', { group: 'fast', hz: DEFAULT_FAST_HZ }).catch(function () {});
  sendCommand('rates.set', { group: 'contact', hz: DEFAULT_CONTACT_HZ }).catch(function () {});
  sendCommand('rates.set', { group: 'mapShowPlayerNames', wname: 'off', on: false }).catch(function () {});
};

fetch('/rates-config')
  .then(function (r) { return r.json(); })
  .then(function (cfg) {
    setSlider('tlm', cfg.fastHz);
    setSlider('contact', cfg.contactHz);
    setShowPlayerNames(cfg.mapShowPlayerNames);
    panelEl.classList.remove('unavailable');
  })
  .catch(function () { panelEl.classList.add('unavailable'); });
