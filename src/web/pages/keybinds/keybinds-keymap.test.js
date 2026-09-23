// Self-check for the KEY page's key naming. Run: `node keybinds-keymap.test.js`.
//
// Two ways this breaks quietly. codeToKey writes a name straight into the plugin's config
// (keybind.set-key), so a wrong one persists to the .cfg and the bind simply never fires — the page
// still looks like it took the keypress. displayKey is an ORDER-DEPENDENT chain of replaces, where
// moving the camelCase split earlier turns "NUM DIVIDE" into "KEYPAD DIVIDE" without any error.
//
// The pairing is the real invariant: every name codeToKey can emit must render through displayKey.
const assert = require('assert');
const { CODE2KEY, codeToKey, displayKey, displayName, eventToKey, eventKeys, captureStep } = require('./keybinds-keymap.js');

// ── codeToKey: the mechanical families ───────────────────────────────────────────────────
assert.strictEqual(codeToKey('KeyA'), 'A', 'KeyA should be A');
assert.strictEqual(codeToKey('KeyZ'), 'Z', 'KeyZ should be Z');
assert.strictEqual(codeToKey('Digit0'), 'Alpha0', 'digits get the Alpha prefix');
assert.strictEqual(codeToKey('Digit9'), 'Alpha9', 'digits get the Alpha prefix');
assert.strictEqual(codeToKey('Numpad7'), 'Keypad7', 'numpad digits get the Keypad prefix');
assert.strictEqual(codeToKey('F1'), 'F1', 'F1 passes through');
assert.strictEqual(codeToKey('F15'), 'F15', 'F15 is the top of the supported range');

// ── codeToKey: not bindable → null, so the caller can ignore the press ───────────────────
// Unity's KeyCode has no F16+, and these must not be stored as a name the plugin cannot resolve.
assert.strictEqual(codeToKey('F16'), null, 'F16 is beyond Unity KeyCode and must be refused');
assert.strictEqual(codeToKey('F0'), null, 'there is no F0');
assert.strictEqual(codeToKey('Escape'), null, 'Escape is reserved for cancelling capture');
assert.strictEqual(codeToKey('MetaLeft'), null, 'unmapped keys must be refused, not guessed');
assert.strictEqual(codeToKey('Keya'), null, 'the Key form is case-sensitive');
assert.strictEqual(codeToKey(''), null, 'empty code should be refused');

// ── displayKey: the compact forms ────────────────────────────────────────────────────────
assert.strictEqual(displayKey('Alpha1'), '1', 'Alpha prefix is dropped entirely');
assert.strictEqual(displayKey('Keypad1'), 'NUM 1', 'Keypad becomes NUM');
assert.strictEqual(displayKey('LeftShift'), 'L-SHIFT', 'Left contracts to L-');
assert.strictEqual(displayKey('RightControl'), 'R-CONTROL', 'Right contracts to R-');
assert.strictEqual(displayKey('LeftArrow'), 'L-ARROW', 'the arrow keys contract too');
assert.strictEqual(displayKey('UpArrow'), 'UP ARROW', 'a name with no Left/Right just splits');
assert.strictEqual(displayKey('BackQuote'), 'BACK QUOTE', 'camelCase splits into words');
assert.strictEqual(displayKey('PageDown'), 'PAGE DOWN', 'camelCase splits into words');
assert.strictEqual(displayKey('Space'), 'SPACE', 'a single word just uppercases');

// The ordering trap: Keypad must be rewritten before the camelCase split, or these read
// "KEYPAD DIVIDE" instead of "NUM DIVIDE".
assert.strictEqual(displayKey('KeypadDivide'), 'NUM DIVIDE', 'Keypad rewrite must precede the camelCase split');
assert.strictEqual(displayKey('KeypadPeriod'), 'NUM PERIOD', 'Keypad rewrite must precede the camelCase split');

// LeftBracket is contracted, but a Left* name outside the alternation must fall through to the
// plain split rather than being contracted by accident.
assert.strictEqual(displayKey('LeftBracket'), 'L-BRACKET', 'LeftBracket is in the contraction list');
assert.strictEqual(displayKey('LeftFoo'), 'LEFT FOO', 'a Left* name outside the list should just split');

// ── The pairing: everything codeToKey emits must render ──────────────────────────────────
// A name added to CODE2KEY that displayKey mangles would show as an empty or ugly bind row, which
// is exactly the kind of thing nobody notices until they open the page looking for that key.
const emitted = new Set(Object.values(CODE2KEY));
for (const c of 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') emitted.add(codeToKey('Key' + c));
for (let d = 0; d <= 9; d++) { emitted.add(codeToKey('Digit' + d)); emitted.add(codeToKey('Numpad' + d)); }
for (let f = 1; f <= 15; f++) emitted.add(codeToKey('F' + f));

for (const name of emitted) {
  assert.ok(typeof name === 'string' && name.length > 0, `codeToKey emitted a bad name: ${name}`);
  const shown = displayKey(name);
  assert.ok(shown.length > 0, `displayKey('${name}') rendered empty`);
  assert.strictEqual(shown, shown.toUpperCase(), `displayKey('${name}') left lowercase: ${shown}`);
  assert.ok(!/[a-z]/.test(shown), `displayKey('${name}') left camelCase unsplit: ${shown}`);
}

// Every CODE2KEY entry should be reachable through codeToKey — an entry the mechanical branches
// shadow is dead weight, and one that returns something else is a table/branch disagreement.
for (const [code, name] of Object.entries(CODE2KEY))
  assert.strictEqual(codeToKey(code), name, `codeToKey('${code}') should give '${name}'`);

// ── modifier chords ──────────────────────────────────────────────────────────────────────
const ev = (type, code, mods) => Object.assign({ type, code, ctrlKey: false, altKey: false, shiftKey: false }, mods);
assert.strictEqual(eventToKey(ev('keydown', 'Digit1', { altKey: true })), 'LeftAlt+Alpha1', 'Alt+1 is a chord');
assert.strictEqual(eventToKey(ev('keydown', 'KeyS', { shiftKey: true, ctrlKey: true })), 'LeftControl+LeftShift+S',
  'modifiers are stored in fixed Control, Alt, Shift order');
assert.strictEqual(eventToKey(ev('keydown', 'AltRight', { altKey: true })), 'RightAlt', 'a lone modifier names only itself');
assert.strictEqual(eventToKey(ev('keydown', 'MetaLeft', {})), null, 'unmappable keys stay unbindable');
assert.deepStrictEqual(eventKeys(ev('keydown', 'Digit1', { altKey: true })), ['LeftAlt+Alpha1', 'Alpha1'],
  'lookup tries the chord first, then the bare key');
assert.deepStrictEqual(eventKeys(ev('keydown', 'Digit1', {})), ['Alpha1'], 'no modifiers: just the key');

// capture: a modifier alone keeps listening; the next key finishes the chord; releasing the
// modifier first finishes as the lone modifier.
assert.deepStrictEqual(captureStep(ev('keydown', 'AltLeft', { altKey: true })), { pending: 'ALT+…' });
assert.deepStrictEqual(captureStep(ev('keydown', 'Digit1', { altKey: true })), { key: 'LeftAlt+Alpha1' });
assert.deepStrictEqual(captureStep(ev('keyup', 'AltLeft', {})), { key: 'LeftAlt' });
assert.deepStrictEqual(captureStep(ev('keyup', 'AltLeft', { ctrlKey: true })), { key: 'LeftControl+LeftAlt' });
assert.strictEqual(captureStep(ev('keyup', 'KeyA', {})), null, 'a plain key release is ignored');

assert.strictEqual(displayName('LeftAlt+Alpha1'), 'ALT+1');
assert.strictEqual(displayName('LeftControl+LeftShift+F5'), 'CTRL+SHIFT+F5');
assert.strictEqual(displayName('LeftAlt'), 'L-ALT', 'a lone modifier keeps its side');

console.log(`keybinds-keymap.test.js: OK (${emitted.size} key names render)`);
