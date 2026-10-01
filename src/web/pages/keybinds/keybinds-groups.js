// The KEY page's grouping and bind-text logic, split out of keybinds.js so it carries no DOM refs and
// can be unit-checked in Node (see keybinds-groups.test.js).
(function (root) {
  // The registry's sections are persistence-shaped; the page reads them in flight order instead.
  // Each group lists sub-sections that claim binds by server section title or by id. `note` names
  // the server section whose shared-behaviour note applies when it isn't the sub-section's own
  // title. A bind no sub-section claims lands in a trailing OTHER group under its own section, so a
  // newly registered keybind shows up without touching this table.
  function sec(t, pick, noteFrom) { return { t: t, pick: pick, noteFrom: noteFrom || t }; }
  function inSection(title) { return function (b) { return b.section === title; }; }
  function idMatches(re) { return function (b) { return re.test(b.id); }; }

  var GROUPS = [
    { id: 'systems', n: '01', t: 'SYSTEMS', secs: [
      sec('POWER · ENGINE · RADAR', idMatches(/^(power|engine|radar)-/)),
      sec('MASTER ARM', idMatches(/^master-arms-/)),
      sec('GEAR', inSection('GEAR')) ] },
    { id: 'combat', n: '02', t: 'COMBAT', secs: [
      sec('COMBAT MODE', idMatches(/^combat-mode-/), 'IMMERSION OPTIONS'),
      sec('WEAPONS', inSection('WEAPONS')),
      sec('COUNTERMEASURES', inSection('COUNTERMEASURES')) ] },
    { id: 'sensors', n: '03', t: 'SENSORS', secs: [
      sec('TGT', inSection('TGT')),
      sec('TGP', inSection('TGP')) ] },
    { id: 'displays', n: '04', t: 'DISPLAY CONTROL', secs: [
      sec('SOI', inSection('SOI')),
      sec('CURSOR', inSection('CURSOR')) ] },
    { id: 'nav', n: '05', t: 'NAVIGATION', secs: [
      sec('MAP', inSection('MAP')) ] },
    { id: 'setup', n: '06', t: 'MFD SETUP', secs: [
      sec('LAYOUT', inSection('LAYOUT')),
      sec('LAYOUT PRESETS', inSection('LAYOUT PRESETS')),
      sec('HUD PRESETS', inSection('HUD PRESETS')),
      sec('TGT PRESETS', inSection('TGT PRESETS')),
      sec('MISC', function (b) { return b.id === 'units-toggle' || b.id === 'internal-mfd-poc-toggle'; }) ] }
  ];

  function isBound(b) {
    return !!b.key || (b.joyButton !== undefined && b.joyButton >= 0) || (b.axis !== undefined && b.axis >= 0);
  }

  function joyText(b) {
    return b.joyNum > 0 ? 'J' + b.joyNum + ' B' + b.joyButton : 'JOY ' + b.joyButton;
  }

  function axisText(b) {
    return b.axisNum > 0 ? 'J' + b.axisNum + ' A' + b.axis : 'AXIS ' + b.axis;
  }

  // What the search box matches against: label, description and whichever bindings are set.
  function searchText(b, displayName) {
    var parts = [b.label, b.description || ''];
    if (b.key) parts.push(displayName(b.key));
    if (b.joyButton >= 0) parts.push(joyText(b));
    if (b.axis >= 0) parts.push(axisText(b));
    return parts.join(' ').toLowerCase();
  }

  // binds → [{id, n, t, secs:[{id, t, note, rows, bound}], bound, all}] — unfiltered, so the rail and
  // the header count stay the same while searching. `notes` is {server section title: note}.
  function build(binds, notes) {
    var claimed = {};
    var groups = GROUPS.map(function (g) {
      return { id: g.id, n: g.n, t: g.t, secs: g.secs.map(function (s, i) {
        var rows = binds.filter(function (b) {
          if (claimed[b.id] || !s.pick(b)) return false;
          claimed[b.id] = true;
          return true;
        });
        return { id: g.id + '-' + i, t: s.t, note: notes[s.noteFrom] || '', rows: rows };
      }) };
    });
    var rest = binds.filter(function (b) { return !claimed[b.id]; });
    if (rest.length) {
      var titles = [];
      rest.forEach(function (b) { if (titles.indexOf(b.section) < 0) titles.push(b.section); });
      groups.push({ id: 'other', n: '07', t: 'OTHER', secs: titles.map(function (t, i) {
        return { id: 'other-' + i, t: t, note: notes[t] || '',
                 rows: rest.filter(function (b) { return b.section === t; }) };
      }) });
    }
    groups.forEach(function (g) {
      g.secs = g.secs.filter(function (s) { return s.rows.length; });
      g.secs.forEach(function (s) { s.bound = s.rows.filter(isBound).length; });
      g.all = g.secs.reduce(function (n, s) { return n + s.rows.length; }, 0);
      g.bound = g.secs.reduce(function (n, s) { return n + s.bound; }, 0);
    });
    return groups.filter(function (g) { return g.secs.length; });
  }

  const api = { GROUPS, build, isBound, joyText, axisText, searchText };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.KeybindsGroups = api;
})(typeof self !== 'undefined' ? self : this);
