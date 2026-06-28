// Shared sender for the inbound command channel (POST /command), used by the MAP page (target
// select on a map tap) and the MFD shell (target deselect from a TGL bezel key). Linked as a
// classic <script> before each page's own script, so `sendCommand` is a plain global.
//
// The wire envelope is FLAT — { cmd, ...args } — because the game's JsonUtility reliably parses
// top-level fields but NOT nested objects in Mono (a nested args.id silently stayed 0). See
// src/plugin/CommandDispatcher.cs.
//
// Returns the raw fetch promise (no built-in .catch) so callers can inspect the response
// (e.g. the MAP tap reacts to !ok) and attach their own rejection handling. Fire-and-forget
// callers should add `.catch(function(){})` to avoid an unhandled rejection on a network error.
function sendCommand(cmd, args) {
  return fetch('/command', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(Object.assign({ cmd: cmd }, args || {}))
  });
}
