// ENG page (docs/eng-page.md) display rules for the plugin's `eng` block: engine labels and
// output readouts, tank labels, and the fuel burn / endurance / range readouts. Pure, so
// avn-eng-policy.test.js runs it under plain node.
(function (root) {
  const KG_TO_LB = 2.20462;
  const M_PER_NM = 1852;
  // Below this ground speed the aircraft is taxiing or parked: endurance still holds, but a
  // range figure would just be endurance times a crawl.
  const RANGE_MIN_GS = 10;

  // Engines arrive sorted left to right (PropulsionReader).
  function engineLabels(n) {
    if (n === 1) return ['ENG'];
    if (n === 2) return ['L ENG', 'R ENG'];
    const out = [];
    for (let i = 0; i < n; i++) out.push('ENG ' + (i + 1));
    return out;
  }

  // How many engine cards sit left of the throttle/heat pedestal.
  function pedestalSplit(n) { return Math.ceil(n / 2); }

  // Thrust when the engine reports a max thrust; turboshafts report 0 thrust and drive a rotor,
  // so they show shaft power instead (the same choice the game's cockpit EngineTelemetry makes).
  function engineOutput(e) {
    if (e && e.thrMax > 0) {
      return { label: 'THR', value: clamp01(e.thr / e.thrMax), text: Math.round(e.thr / 1000) + ' kN' };
    }
    if (e && e.pwrMax > 0) {
      return { label: 'PWR', value: clamp01(e.pwr / e.pwrMax), text: Math.round(e.pwr / 1000) + ' kW' };
    }
    return { label: 'THR', value: -1, text: '--' };
  }

  function engineStatus(st) {
    if (st === 2) return { cls: 'inop', text: 'INOP' };
    if (st === 1) return { cls: 'fault', text: 'FAULT' };
    return { cls: 'ok', text: 'OK' };
  }

  // GameObject name -> a short bar label: each word cut to 4 letters, 8 characters in all, so
  // "fuselage_F" reads "FUSE F" rather than a clipped "FUSELAGE".
  function tankLabel(name) {
    const words = String(name || '').split(/[_\-.\s]+/).filter(Boolean);
    const s = words.map(function (w) { return w.slice(0, 4); }).join(' ').toUpperCase();
    return s ? s.slice(0, 8).trim() : 'TANK';
  }

  function fuelReadouts(eng, metric) {
    const massK = metric ? 1 : KG_TO_LB;
    const massUnit = metric ? 'kg' : 'lb';
    const fuelKg = eng && eng.fuelKg >= 0 ? eng.fuelKg : -1;
    const burn = eng && typeof eng.burn === 'number' ? eng.burn : -1;   // kg/s
    const gs = eng && eng.gs > 0 ? eng.gs : 0;                          // m/s

    const out = {
      fuel: fuelKg >= 0 ? group(fuelKg * massK) : '--',
      fuelUnit: massUnit,
      burn: '--', burnUnit: massUnit + '/min',
      endurance: '--:--',
      range: '--', rangeUnit: metric ? 'km' : 'nm',
      gs: '--', gsUnit: metric ? 'km/h' : 'kt',
    };
    out.gs = Math.round(metric ? gs * 3.6 : gs * 3600 / M_PER_NM).toString();
    if (burn < 0 || fuelKg < 0) return out;

    out.burn = Math.round(burn * 60 * massK).toString();
    // A zero burn (engines off) has no meaningful endurance either.
    if (burn <= 0) return out;
    const enduranceS = fuelKg / burn;
    out.endurance = hmm(enduranceS);
    if (gs >= RANGE_MIN_GS) out.range = group(enduranceS * gs / (metric ? 1000 : M_PER_NM));
    return out;
  }

  function hmm(seconds) {
    const totalMin = Math.floor(seconds / 60);
    const h = Math.floor(totalMin / 60), m = totalMin % 60;
    return h > 99 ? '99:59' : h + ':' + (m < 10 ? '0' : '') + m;
  }

  // 3420 -> "3,420"; locale-free so the readout never switches separators with the browser.
  function group(x) {
    return Math.round(x).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  }

  function clamp01(x) { return !(x > 0) ? 0 : x > 1 ? 1 : x; }

  const api = { KG_TO_LB, engineLabels, pedestalSplit, engineOutput, engineStatus, tankLabel, fuelReadouts };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnEngPolicy = api;
})(typeof self !== 'undefined' ? self : this);
