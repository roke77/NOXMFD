// Self-check that named SSE events the server emits from a squadmate's shared data
// (SseHub.cs's /stream handler) match the event name telemetry-source.js actually
// listens for. Run: `node sse-event-coverage.test.js`.
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

// The squad-data forward: `Squadron.SendData`'s payload arrives here as one SSE event per message,
// built as a plain string literal (unlike the dynamic `"event: ext-" + kv.Key` extension events,
// which have no single fixed name to check against a listener).
const serverMatch = serverSrc.match(/Squad\.DataSince[\s\S]*?"event: ([a-zA-Z-]+)\\n/);
assert.ok(serverMatch, 'could not find the squad-data SSE event literal in SseHub.cs — did that block move or get renamed?');
const serverEvent = serverMatch[1];

const clientHasListener = clientSrc.includes(`addEventListener('${serverEvent}',`);
assert.ok(clientHasListener,
    `TelemetryServer.cs emits "event: ${serverEvent}" for squad-shared data, but telemetry-source.js ` +
    `has no es.addEventListener('${serverEvent}', ...) — a shared route would silently never reach the shell`);

console.log(`sse-event-coverage.test.js: OK (squad-data event "${serverEvent}" has a matching listener)`);
