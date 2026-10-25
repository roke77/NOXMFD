// F-35 master strip widgets (docs/f35-strip-widgets.md) — pure, DOM-free, so f35-strip.test.js can
// check it in Node. Between the fixed brand block (left) and the fixed WAKE/FULLSCREEN/EDIT group
// (right) the strip has SLOTS widget slots; its arrangement is one widget id (or null, empty) per
// slot. f35.js owns the elements and only re-parents them into their slots.
(function (root) {
  var SLOTS = 4;

  // Every widget a slot can hold. The ADD list shows them by label, A-Z (available).
  var CATALOG = [
    { id: 'gauges',   label: 'THRL / FUEL' },
    { id: 'engine',   label: 'RPM / HEAT' },
    { id: 'flags',    label: 'AVIONICS FLAGS' },
    { id: 'wpn',      label: 'WPN MODES' },
    { id: 'flight',   label: 'FLIGHT DATA' },
    { id: 'clocks',   label: 'CLOCKS' },
  ];
  // The readouts inside the two readout widgets, left to right: FLIGHT DATA from the 'adi' slice
  // (readout below), CLOCKS from the browser clock and the 'mis' slice (localClock/misClock).
  var FLIGHT = ['alt', 'agl', 'tas', 'hdg', 'vs', 'g', 'mach'];
  var CLOCKS = ['clock', 'mis-time', 'mis-dur'];

  // WPN MODES' two switches, their states left to right as drawn. A press moves to the next state
  // to the right, wrapping from the last back to the first. The ids are the wire values: MODE's
  // are combat-mode.set's groups; MASTER's map to master-arms.set's on flag in f35.js.
  var SWITCHES = { master: ['safe', 'arm'], mode: ['aa', 'all', 'ag'] };
  // An unknown current state (no data yet) steps to the first one.
  function nextState(sw, cur) {
    var s = SWITCHES[sw];
    return s[(s.indexOf(cur) + 1) % s.length];
  }
  var KNOWN = {};
  CATALOG.forEach(function (w) { KNOWN[w.id] = w; });

  // A fresh window's strip: empty.
  var DEFAULT = [null, null, null, null];

  // A stored or preset-carried arrangement → exactly SLOTS entries, each a known id or null, no id
  // twice (a widget is one element, so it can only be in one slot). null when `slots` isn't an
  // array at all, so a preset saved before the strip was editable leaves the strip alone.
  function normalize(slots) {
    if (!Array.isArray(slots)) return null;
    var seen = {}, out = [];
    for (var i = 0; i < SLOTS; i++) {
      var id = slots[i];
      var ok = typeof id === 'string' && !!KNOWN[id] && !seen[id];
      if (ok) seen[id] = true;
      out.push(ok ? id : null);
    }
    return out;
  }

  // Puts `id` in slot i. A widget already in another slot moves out of it, so a slot list never
  // holds one twice.
  function place(slots, i, id) {
    if (!KNOWN[id] || i < 0 || i >= slots.length) return slots.slice();
    return slots.map(function (x, j) { return j === i ? id : (x === id ? null : x); });
  }

  function clear(slots, i) { return slots.map(function (x, j) { return j === i ? null : x; }); }

  // What the ADD list offers: catalog widgets in no slot, alphabetical by label.
  function available(slots) {
    return CATALOG.filter(function (w) { return slots.indexOf(w.id) === -1; })
      .sort(function (a, b) { return a.label < b.label ? -1 : a.label > b.label ? 1 : 0; });
  }

  function pad(n) { return (n < 10 ? '0' : '') + n; }
  // Whole seconds → HH:MM:SS, the format MIS shows (mis.js fmtHms, which takes hours).
  function hms(seconds) {
    var t = Math.max(0, Math.round(seconds));
    return pad(Math.floor(t / 3600)) + ':' + pad(Math.floor(t / 60) % 60) + ':' + pad(t % 60);
  }

  // A flight-data readout's {label, value} from the 'adi' slice. `units` is AdiMath.units(metric)
  // and `bearing` WptRoute.formatBearing — passed in so this file stays dependency-free. The label
  // carries the unit, so the value can stay a bare number.
  function readout(id, m, units, bearing) {
    var on = m && m.present === true;
    function num(v, k, digits) { return on && v != null ? (v * k).toFixed(digits) : '--'; }
    switch (id) {
      case 'alt':  return { label: 'ALT ' + units.alt.unit, value: num(m && m.alt, units.alt.k, 0) };
      case 'agl':  return { label: 'R-ALT ' + units.alt.unit, value: num(m && m.agl, units.alt.k, 0) };
      case 'tas':  return { label: 'TAS ' + units.spd.unit, value: num(m && m.tas, units.spd.k, 0) };
      case 'hdg':  return { label: 'HDG', value: on && m.hdg != null ? bearing(m.hdg) : '--' };
      case 'vs':   return { label: 'V/S ' + units.vs.unit,
                            value: on && m.vs != null
                              ? signed(Math.round(m.vs * units.vs.k / units.vs.round) * units.vs.round) : '--' };
      case 'g':    return { label: 'G', value: num(m && m.g, 1, 1) };
      case 'mach': return { label: 'MACH', value: num(m && m.mach, 1, 2) };
    }
    return null;
  }
  function signed(n) { return (n > 0 ? '+' : '') + n; }

  // The 'mis' slice's clocks: tod is the mission's time of day in hours, duration its elapsed
  // seconds. present:false in multiplayer or between missions.
  function misClock(id, m) {
    var on = m && m.present === true;
    if (id === 'mis-time') return { label: 'MSN TIME', value: on ? hms((m.tod || 0) * 3600) : '--:--:--' };
    if (id === 'mis-dur')  return { label: 'ELAPSED',  value: on ? hms(m.duration || 0) : '--:--:--' };
    return null;
  }

  function localClock(date) {
    return { label: 'LOCAL', value: pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds()) };
  }

  // WPN MODES' selected-weapon block, from the 'loadout' slice's items ({n, a, f}: name, rounds
  // left, capacity) and selWeapon. The gauge follows WPN's rule (wpn.js PIP_MAX/renderBar): one pip
  // per round up to 12 rounds of capacity, a proportional fill above that, none without a
  // capacity. null when nothing is selected or the selection isn't in the loadout.
  var PIP_MAX = 12;
  function selectedAmmo(l) {
    var name = l && l.selWeapon;
    var w = name && (l.items || []).filter(function (x) { return x && x.n === name; })[0];
    if (!w) return null;
    var a = Math.max(0, w.a | 0), f = Math.max(0, w.f | 0);
    return { name: w.n, a: a, f: f, bar: f === 0 ? 'none' : f <= PIP_MAX ? 'pips' : 'fill',
             empty: f > 0 && a === 0 };
  }

  var api = { SLOTS: SLOTS, CATALOG: CATALOG, FLIGHT: FLIGHT, CLOCKS: CLOCKS, DEFAULT: DEFAULT,
              SWITCHES: SWITCHES, nextState: nextState, selectedAmmo: selectedAmmo,
              normalize: normalize, place: place,
              clear: clear, available: available, hms: hms, readout: readout, misClock: misClock,
              localClock: localClock };
  root.F35Strip = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
