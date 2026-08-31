// Self-check for the fixed squad-callsign list (issue #42). Run: `node callsigns.test.js`.
// ES module, loaded with dynamic import() (same reasoning as services/pad-cursor.test.js) — needs
// Node >= 22.7.
const assert = require('assert');

(async () => {
  const { SQUAD_CALLSIGNS } = await import('./callsigns.js');

  assert.ok(Array.isArray(SQUAD_CALLSIGNS) && SQUAD_CALLSIGNS.length > 0, 'non-empty array');

  // Every option is what a <select> should show: a plain, non-empty, all-caps string with no
  // leading/trailing whitespace (a stray space would render as an invisible, unselectable-looking
  // gap in the option list).
  SQUAD_CALLSIGNS.forEach((c) => {
    assert.strictEqual(typeof c, 'string', `${c} is not a string`);
    assert.ok(c.length > 0, 'no empty callsign');
    assert.strictEqual(c, c.toUpperCase(), `${c} is not upper case`);
    assert.strictEqual(c, c.trim(), `${c} has leading/trailing whitespace`);
  });

  // No duplicates — a repeated entry would just be dead weight in the dropdown.
  assert.strictEqual(new Set(SQUAD_CALLSIGNS).size, SQUAD_CALLSIGNS.length, 'no duplicate callsigns');

  // Every entry fits Squad.CreateSquad's own 20-char cap (Squad.cs) — a callsign the dropdown
  // itself can't actually create would be a silent no-op when picked.
  SQUAD_CALLSIGNS.forEach((c) => {
    assert.ok(c.length <= 20, `${c} exceeds Squad.CreateSquad's 20-char limit`);
  });

  console.log(`callsigns.test.js: OK (${SQUAD_CALLSIGNS.length} callsigns)`);
})();
