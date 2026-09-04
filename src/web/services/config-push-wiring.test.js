// Structural coverage for the keybind/HUD polling replacement. Run with the other *.test.js files.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

function read(relative) {
  return fs.readFileSync(path.join(__dirname, '..', '..', '..', relative), 'utf8');
}

const remote = read('src/web/services/remote-keybinds.js');
const layout = read('src/web/shell/shared/layout-keybinds.js');
const keys = read('src/web/pages/keybinds/keybinds.js');
const hud = read('src/web/pages/hud/hud.js');
const classic = read('src/web/shell/classic/mfd.js');
const f35 = read('src/web/shell/f35/f35.js');
const preview = read('tools/preview-mock.js');

assert.ok(!remote.includes('BIND_POLL_MS'), 'remote keybinds must not restore periodic config polling');
assert.ok(!layout.includes("fetch('/keybinds-config'"), 'layout keys must reuse the shared config snapshot');
assert.ok(keys.includes('if (capturing && capturePollTimer == null)'), 'KEY fallback polling must be capture-scoped');
assert.ok(!hud.includes('setInterval(load'), 'HUD must not restore periodic options polling');
assert.ok(classic.includes("'hud-options-push': { page: 'hud'"), 'classic shell must relay HUD options');
assert.ok(f35.includes("hud: ['hud-options-push']"), 'F-35 shell must relay HUD options');
assert.ok(preview.includes("fetch('/__preview-push'"), 'preview SSE must use its combined change-gated endpoint');

console.log('config-push-wiring.test.js: OK');
