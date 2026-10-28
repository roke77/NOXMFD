// Self-check for the battle-damage page rules. Run: `node battle-damage.test.js`.
const assert = require('assert');

function fakeDoc() {
  const body = { children: [], appendChild(c) { this.children.push(c); } };
  return {
    body,
    createElement() {
      const cls = new Set();
      return { hidden: false, textContent: '', classList: {
        toggle(c, on) { if (on) cls.add(c); else cls.delete(c); }, contains: (c) => cls.has(c) } };
    },
  };
}

(async () => {
  const { failedSystem, laserFailed, createFailScreen } = await import('./battle-damage.js');
  const st = (o) => Object.assign({ on: true, radar: 'ok', tgp: 'ok', laser: 'ok' }, o);

  // Page → system.
  assert.strictEqual(failedSystem('rdr', st({ radar: 'fail' })), 'radar');
  assert.strictEqual(failedSystem('tgp', st({ tgp: 'fail' })), 'tgp');
  assert.strictEqual(failedSystem('tgp', st({ radar: 'fail' })), null, 'TGP does not fail with the radar');
  for (const page of ['hsd', 'adi', 'hsi', 'hud', 'avn', 'main', 'rwr', 'lyt', 'ui'])
    assert.strictEqual(failedSystem(page, st({ radar: 'fail', tgp: 'fail', laser: 'fail' })), null, page + ' never fails');

  // Not fitted is not failed; option off changes nothing; no status yet is not failed.
  assert.strictEqual(failedSystem('rdr', st({ radar: 'none' })), null);
  assert.strictEqual(failedSystem('rdr', st({ on: false, radar: 'fail' })), null);
  assert.strictEqual(failedSystem('rdr', null), null);

  // TGP partial: laser only while the camera still works.
  assert.strictEqual(laserFailed(st({ laser: 'fail' })), true);
  assert.strictEqual(laserFailed(st({ laser: 'fail', tgp: 'fail' })), false, 'a dead camera is TGP FAIL, not LASER FAIL');
  assert.strictEqual(laserFailed(st({ laser: 'none' })), false);
  assert.strictEqual(laserFailed(st({ on: false, laser: 'fail' })), false);

  // Fail screen: a page opened already failed settles without flicker; a later loss flickers once.
  let doc = fakeDoc(), update = createFailScreen(doc, 'rdr'), el = doc.body.children[0];
  update(st({ radar: 'fail' }));
  assert.strictEqual(el.hidden, false);
  assert.strictEqual(el.textContent, 'RADAR FAIL');
  assert.strictEqual(el.classList.contains('bd-flicker'), false);

  doc = fakeDoc(); update = createFailScreen(doc, 'rdr'); el = doc.body.children[0];
  update(st());
  assert.strictEqual(el.hidden, true);
  update(st({ radar: 'fail' }));
  assert.strictEqual(el.classList.contains('bd-flicker'), true);
  update(st());   // repaired / respawned
  assert.strictEqual(el.hidden, true);
  assert.strictEqual(el.classList.contains('bd-flicker'), false);

  console.log('battle-damage.test.js: OK');
})().catch((e) => { console.error(e); process.exit(1); });
