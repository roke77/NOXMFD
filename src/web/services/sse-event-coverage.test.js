// Self-check that every fixed-name SSE event the server emits (SseHub.cs's /stream handler) has a
// matching es.addEventListener(...) in telemetry-source.js. Run: `node sse-event-coverage.test.js`.
//
// This is exactly the shape of bug that shipped once already: the server emitted
// `event: sqd-data` while the client listened for `es.addEventListener('squadron', ...)` — an
// EventSource with the wrong listener name never fires, so a leader's shared route silently never
// reached the browser. No .cs build/execution here — a plain text scan of both literals, the same
// spirit as server-route-coverage.test.js.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const serverPath = path.join(__dirname, '..', '..', 'plugin', 'Http', 'SseHub.cs');
const serverSrc = fs.readFileSync(serverPath, 'utf8');
const clientSrc = fs.readFileSync(path.join(__dirname, 'telemetry-source.js'), 'utf8');

// Every `"event: <name>\n` FIXED string literal — excludes the one dynamic case
// (`"event: ext-" + kv.Key`, a runtime-registered set of names with no single literal to check).
const matches = [...serverSrc.matchAll(/"event: ([a-zA-Z-]+)\\n/g)];
assert.ok(matches.length > 0, 'found no "event: <name>\\n" SSE literals in SseHub.cs — did the whole shape change?');

const names = [...new Set(matches.map(m => m[1]))];
const missing = names.filter(name => !clientSrc.includes(`addEventListener('${name}',`));

assert.strictEqual(missing.length, 0,
    `SseHub.cs emits ${missing.map(n => `"event: ${n}"`).join(', ')}, but telemetry-source.js has no ` +
    `matching es.addEventListener(...) — that data would silently never reach the shell`);

console.log(`sse-event-coverage.test.js: OK (${names.length} SSE events all have a matching listener: ${names.join(', ')})`);
