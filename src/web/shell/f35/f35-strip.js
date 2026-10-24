// F-35 master strip widgets (docs/f35-strip-widgets.md) — pure, DOM-free, so f35-strip.test.js can
// check it in Node. The strip's arrangement is an ordered list of widget ids; f35.js owns the
// elements (each declared once in f35.html under its data-widget id) and only re-parents them in
// this order.
(function (root) {
  // Every widget the strip can show, in the order the ADD list offers them. `label` is the name
  // the ADD list and edit mode show.
  var CATALOG = [
    { id: 'logo',     label: 'NO XMFD LOGO' },
    { id: 'net',      label: 'CONNECTION' },
    { id: 'thr',      label: 'THRL GAUGE' },
    { id: 'fuel',     label: 'FUEL GAUGE' },
    { id: 'alt',      label: 'ALT' },
    { id: 'agl',      label: 'R-ALT' },
    { id: 'tas',      label: 'TAS' },
    { id: 'hdg',      label: 'HDG' },
    { id: 'vs',       label: 'V/S' },
    { id: 'g',        label: 'G' },
    { id: 'mach',     label: 'MACH' },
    { id: 'clock',    label: 'LOCAL TIME' },
    { id: 'mis-time', label: 'MISSION TIME' },
    { id: 'mis-dur',  label: 'MISSION ELAPSED' },
    { id: 'spacer',   label: 'SPACER' },
    { id: 'gear',     label: 'GEAR FLAG' },
    { id: 'radar',    label: 'RADAR FLAG' },
    { id: 'guns',     label: 'GUNS FLAG' },
    { id: 'eng',      label: 'ENG FLAG' },
    { id: 'assist',   label: 'ASSIST FLAG' },
    { id: 'nvg',      label: 'NVG FLAG' },
    { id: 'lights',   label: 'LIGHTS FLAG' },
    { id: 'turret',   label: 'TURRET FLAG' },
    { id: 'wake',     label: 'WAKE LOCK' },
    { id: 'fll',      label: 'FULLSCREEN' },
  ];
  var KNOWN = {};
  CATALOG.forEach(function (w) { KNOWN[w.id] = w; });

  // The strip a fresh browser starts with: the fixed strip this feature replaced, with the spacer
  // where the flags' margin-left:auto used to push them right.
  var DEFAULT = ['logo', 'net', 'thr', 'fuel', 'spacer',
                 'gear', 'radar', 'guns', 'eng', 'assist', 'nvg', 'lights', 'turret', 'wake', 'fll'];

  // A stored or preset-carried list → a clean one: known ids only, each once. null when `list`
  // isn't an array at all, so a preset saved before the strip was editable leaves the strip alone.
  // An empty array is valid (a pilot may clear the strip).
  function normalize(list) {
    if (!Array.isArray(list)) return null;
    var seen = {};
    return list.filter(function (id) {
      if (typeof id !== 'string' || !KNOWN[id] || seen[id]) return false;
      seen[id] = true;
      return true;
    });
  }

  // dir -1 = one place left, +1 = one place right. Out-of-range moves return the list unchanged.
  function move(list, id, dir) {
    var i = list.indexOf(id), j = i + dir;
    if (i === -1 || j < 0 || j >= list.length) return list.slice();
    var out = list.slice();
    out[i] = out[j];
    out[j] = id;
    return out;
  }

  function remove(list, id) { return list.filter(function (x) { return x !== id; }); }

  // Inserts after `afterId` (the selected widget), or at the end when nothing is selected.
  function add(list, id, afterId) {
    if (list.indexOf(id) !== -1 || !KNOWN[id]) return list.slice();
    var out = list.slice(), at = out.indexOf(afterId);
    out.splice(at === -1 ? out.length : at + 1, 0, id);
    return out;
  }

  // What the ADD list offers: catalog widgets not already on the strip.
  function available(list) { return CATALOG.filter(function (w) { return list.indexOf(w.id) === -1; }); }

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

  var api = { CATALOG: CATALOG, DEFAULT: DEFAULT, normalize: normalize, move: move, remove: remove,
              add: add, available: available, hms: hms, readout: readout, misClock: misClock,
              localClock: localClock };
  root.F35Strip = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
