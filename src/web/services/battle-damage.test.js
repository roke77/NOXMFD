// Self-check for the battle-damage page rules. Run: `node battle-damage.test.js`.
const assert = require('assert');

function fakeDoc() {
  const body = { children: [], appendChild(c) { this.children.push(c); } };
  return {
    body,
    createElement() { return { hidden: false, textContent: '' }; },
  };
}

(async () => {
  const { failedSystem, laserFailed, createFailScreen } = await import('./battle-damage.js');
  const st = (o) => Object.assign({ on: true, radar: 'ok', tgp: 'ok', laser: 'ok' }, o);

  // Page → system.
  assert.strictEqual(failedSystem('rdr', st({ radar: 'fail' })), 'radar');
  assert.strictEqual(failedSystem('hsd', st({ radar: 'fail' })), 'radar', 'HSD goes down with FCR');
  assert.strictEqual(failedSystem('hsd', st({ tgp: 'fail', laser: 'fail' })), null, 'HSD does not fail with the TGP');
  assert.strictEqual(failedSystem('tgp', st({ tgp: 'fail' })), 'tgp');
  assert.strictEqual(failedSystem('tgp', st({ radar: 'fail' })), null, 'TGP does not fail with the radar');
  for (const page of ['adi', 'hsi', 'hud', 'avn', 'main', 'rwr', 'lyt', 'ui'])
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

  // Fail screen: up with its banner the moment the system is lost, down again once it recovers.
  const doc = fakeDoc(), update = createFailScreen(doc, 'rdr'), el = doc.body.children[0];
  update(st());
  assert.strictEqual(el.hidden, true);
  update(st({ radar: 'fail' }));
  assert.strictEqual(el.hidden, false);
  assert.strictEqual(el.textContent, 'RADAR FAIL');
  update(st());   // repaired / respawned
  assert.strictEqual(el.hidden, true);

  console.log('battle-damage.test.js: OK');
})().catch((e) => { console.error(e); process.exit(1); });
