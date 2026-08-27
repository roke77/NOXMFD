# Server hardening — request hygiene + structural split for `TelemetryServer.cs`

## Status

Partly implemented. Request hygiene, command endpoint/queue extraction, SSE/session extraction,
telemetry JSON extraction, embedded web asset serving, and HTTP route dispatch have shipped. The
remaining structural candidate is the MJPEG handler.

## Where this came from

An external code-review agent did a full local scan of the repo and flagged, among other things,
that `TelemetryServer.cs` (2019 lines) does too much in one file, and that `/command` and
`/ext/<id>/command` are unauthenticated with unbounded body reads. Both are real, but need scoping
against what's already decided elsewhere in this repo before turning into work items — see below.

## What this doc does NOT propose, and why

The review's suggestion to add "optional pairing/token auth" is **out of scope**, not an oversight.
[SECURITY.md](../SECURITY.md)'s "one real caveat" section documents the LAN server's lack of
authentication as a **deliberate** design choice: the whole point of the second-screen feature is a
tablet on the same Wi-Fi connecting with zero friction, and the documented mitigation is already
"leave `AutoSetupLanAccess` off to stay localhost-only," not a login step. Adding token/pairing auth
would be a real UX regression against that stated goal, not a pure hardening win — if that tradeoff
is ever worth revisiting, it needs its own explicit decision, not a bundled line item here.

What *is* in scope below is hygiene that costs nothing against that trust model: rejecting malformed
requests earlier and bounding how much of one this server will read, same as any HTTP server should
regardless of whether the network is trusted.

## Part A — command-endpoint request hygiene

Original findings, now resolved in current code (`src/plugin/Http/CommandEndpoint.cs` and
`src/plugin/Http/TelemetryHttpRouter.cs`):

- **No HTTP method check.** Routing is pure path-matching (`else if (path == "/command")
  HandleCommand(ctx);`, `:713`) — a GET to `/command` reaches the same handler as a POST. Harmless
  today only because a GET has no body to read, but it's an easy, free thing to make explicit.
- **Unbounded body read.** Both `HandleCommand` (`:745`, `ReadToEnd()` at `:751`) and the extension
  command handler (`:869`, `ReadToEnd()` at `:878`) read the entire request body into memory with no
  size cap before parsing. The command queue itself is bounded (`MaxQueuedCommands`), but nothing
  stops a single oversized request from allocating an arbitrarily large string first.

### Shipped fix

1. **Method check on `/command` only.** `/ext/<id>/command` already gates on
   `ctx.Request.HttpMethod == "POST"` before calling `HandleExtCommand`
   (`src/plugin/Http/TelemetryServer.cs`) — verified in current code, nothing to do there. `/command`
   itself has no such check (routing is pure path-matching at `:713`-`:714`); reject non-POST there
   with `405` before touching the body.
2. **Body-size cap on both.** Neither endpoint's method-gating (or lack of it) bounds body size —
   check `ctx.Request.ContentLength64` up front and reject (`413`) anything past a generous ceiling
   (a command envelope is a few hundred bytes at most; something like 16 KB leaves headroom without
   allowing multi-MB bodies). If `ContentLength64` is `-1` (chunked/unknown), cap the `StreamReader`
   read itself rather than trusting the header.
3. Apply the same body-size check to any other endpoint that reads a body from an untrusted caller
   (grep for `ReadToEnd()` — same pattern likely wants the same fix everywhere it appears, not just
   these two call sites).
4. **Content-Type check on both command endpoints.** `/command` and `/ext/<id>/command` now reject
   non-JSON request bodies with `415`. The built-in web clients already send
   `Content-Type: application/json`, so this only rejects malformed/manual requests and the simplest
   HTML-form CSRF shape.

Effort: **XS**. No behavior change for any legitimate client (the web UI already sends small POST
bodies); pure rejection of malformed/oversized input.

### Explicitly out of scope for this item

- Token/pairing auth (see above).
- **Origin checks — worth a mention here, not dropped entirely.** Corrected from an
  earlier draft of this doc: lacking permissive CORS headers stops a hostile page's JS from
  *reading* this server's response cross-origin, but does **not** stop the browser from *sending*
  the cross-origin request in the first place — that's the classic CSRF gap, distinct from the
  "malicious LAN peer sending a request directly" risk SECURITY.md already covers. A page open in
  the same browser, on any origin, could still fire a cross-origin POST to `/command`; its JS
  just can't read the 204 back (and there's nothing sensitive in that response to read anyway, so
  the practical impact is bounded). The shipped JSON `Content-Type` gate blocks the simplest HTML
  `<form>` CSRF vector. A `fetch()`-based origin can still set that header, so this remains request
  hygiene, not token/pairing auth.

## Part B — structural split of `TelemetryServer.cs`

`docs/csharp-unit-testing.md` already scopes and prioritizes extracting the JSON-writer layer
(`Serialize`/`EscapeJson`/`MisBlock`/`ObjBlock`/etc., `:1583`-`:2019`ish) into its own class as **step
2** of that plan — "the single highest-value move," since it's nearly the whole file and has only 6
real game touchpoints. **Don't duplicate that work here — see that doc for the extraction plan and
land it first.**

What remains after the shipped splits is mostly transport runtime state: MJPEG
(`HandleMjpegAsync`), config endpoints (`ServeHudOptions` and its siblings), and extension request
handlers. Candidate further splits, **lowest-risk first**:

| Split | What moves | Risk |
|---|---|---|
| `TelemetryJson.cs` | JSON-writer layer | Done |
| `TelemetryAssets.cs` | `ServeAssetRel` + static-file/embedded-resource serving | Done |
| `TelemetryHttpRouter.cs` | URL dispatch from path to endpoint handler | Done |
| `CommandEndpoint.cs` | `HandleCommand`/`TryDequeueCommand`/`_cmdQueue`/`_cmdLock` + extension command body hygiene | Done |
| `SseHub.cs` | `/stream` connection lifetime, per-client instance registry, hello/cursor/ext SSE events, and `/soi-instances` diagnostics | Done |
| MJPEG handler | `HandleMjpegAsync` | Low — mirrors the SSE split's shape but simpler (no per-client state) |

Don't attempt all of these in one PR — each is independently useful and independently testable via
`dotnet build` + the existing in-game verification checklist (no C# test harness covers this file yet
outside what `docs/csharp-unit-testing.md`'s plan lands).

### Next recommended targets

1. **Consider the MJPEG handler next.** It is smaller, but it shares the same long-lived response
   style; doing it after the SSE split should make the pattern clearer.

## Scope

- [x] Land `TelemetryJson` extraction first (shared prerequisite)
- [x] Add a POST-only check to `/command` (`/ext/<id>/command` already has one)
- [x] Add a body-size cap to both `/command` and `/ext/<id>/command`
- [x] Audit other `ReadToEnd()` call sites for the same body-size check
- [x] `Content-Type: application/json` check on both command endpoints
      as cheap CSRF-style hardening
- [x] Extract embedded asset serving (`TelemetryAssets.cs`)
- [x] Extract HTTP route dispatch (`TelemetryHttpRouter.cs`)
- [x] Extract command endpoint/queue (`CommandEndpoint.cs`)
- [x] Extract SSE/session hub (`SseHub.cs`; live multi-display SOI behavior still wants an in-game/browser spot-check before release)
- [ ] Extract the MJPEG handler after the SSE pattern is clear
- [x] One-line SECURITY.md note once the method/size hardening ships (not a trust-model change, just
      documents that these two checks now exist)
