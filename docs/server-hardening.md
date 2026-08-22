# Server hardening — request hygiene + structural split for `TelemetryServer.cs`

## Status

Partly implemented. Request hygiene, telemetry JSON extraction, embedded web asset serving, and
HTTP route dispatch have shipped. Remaining structural candidates are the command endpoint/queue,
SSE stream hub, and MJPEG handler.

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

Original findings, now resolved in current code (`src/plugin/Http/TelemetryServer.cs` and
`src/plugin/Http/TelemetryHttpRouter.cs`):

- **No HTTP method check.** Routing is pure path-matching (`else if (path == "/command")
  HandleCommand(ctx);`, `:713`) — a GET to `/command` reaches the same handler as a POST. Harmless
  today only because a GET has no body to read, but it's an easy, free thing to make explicit.
- **Unbounded body read.** Both `HandleCommand` (`:745`, `ReadToEnd()` at `:751`) and the extension
  command handler (`:869`, `ReadToEnd()` at `:878`) read the entire request body into memory with no
  size cap before parsing. The command queue itself is bounded (`MaxQueuedCommands`), but nothing
  stops a single oversized request from allocating an arbitrarily large string first.

### Proposed fix

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

Effort: **XS**. No behavior change for any legitimate client (the web UI already sends small POST
bodies); pure rejection of malformed/oversized input.

### Explicitly out of scope for this item

- Token/pairing auth (see above).
- **Origin/Content-Type checks — worth a mention here, not dropped entirely.** Corrected from an
  earlier draft of this doc: lacking permissive CORS headers stops a hostile page's JS from
  *reading* this server's response cross-origin, but does **not** stop the browser from *sending*
  the cross-origin request in the first place — that's the classic CSRF gap, distinct from the
  "malicious LAN peer sending a request directly" risk SECURITY.md already covers. A page open in
  the same browser, on any origin, could still fire a cross-origin POST to `/command` today; its JS
  just can't read the 204 back (and there's nothing sensitive in that response to read anyway, so
  the practical impact is bounded — the command still executes, but nothing leaks back). A cheap,
  optional hardening step: reject `/command`/`/ext/<id>/command` requests whose `Content-Type`
  isn't `application/json` (a plain HTML `<form>` POST can't set that header, which blocks the
  simplest CSRF vector; a `fetch()`-based one still could, since JS can set any header it wants).
  Lower priority than Part A's two fixes — flagged so it doesn't get silently forgotten, not
  because it's not real.

## Part B — structural split of `TelemetryServer.cs`

`docs/csharp-unit-testing.md` already scopes and prioritizes extracting the JSON-writer layer
(`Serialize`/`EscapeJson`/`MisBlock`/`ObjBlock`/etc., `:1583`-`:2019`ish) into its own class as **step
2** of that plan — "the single highest-value move," since it's nearly the whole file and has only 6
real game touchpoints. **Don't duplicate that work here — see that doc for the extraction plan and
land it first.**

What remains after the shipped splits is mostly transport runtime state: command queue/body handling,
SSE (`HandleSseAsync`), MJPEG (`HandleMjpegAsync`), config endpoints (`ServeHudOptions` and its
siblings), and extension request handlers. Candidate further splits, **lowest-risk first**:

| Split | What moves | Risk |
|---|---|---|
| `TelemetryJson.cs` | JSON-writer layer | Done |
| `TelemetryAssets.cs` | `ServeAssetRel` + static-file/embedded-resource serving | Done |
| `TelemetryHttpRouter.cs` | URL dispatch from path to endpoint handler | Done |
| `CommandQueue.cs` | `HandleCommand`/`TryDequeueCommand`/`_cmdQueue`/`_cmdLock` | Low — already a clean unit, just needs to move |
| `SseHub.cs` | `HandleSseAsync` + the per-client SSE loop | Medium — touches the frame-version cache from item #2 of `docs/performance.md`; verify that cache's threading contract survives the move |
| MJPEG handler | `HandleMjpegAsync` | Low — mirrors the SSE split's shape but simpler (no per-client state) |

Don't attempt all of these in one PR — each is independently useful and independently testable via
`dotnet build` + the existing in-game verification checklist (no C# test harness covers this file yet
outside what `docs/csharp-unit-testing.md`'s plan lands).

## Scope

- [x] Land `TelemetryJson` extraction first (shared prerequisite)
- [x] Add a POST-only check to `/command` (`/ext/<id>/command` already has one)
- [x] Add a body-size cap to both `/command` and `/ext/<id>/command`
- [x] Audit other `ReadToEnd()` call sites for the same body-size check
- [ ] (Optional, lower priority) `Content-Type: application/json` check on both command endpoints
      as cheap CSRF-style hardening
- [x] Extract embedded asset serving (`TelemetryAssets.cs`)
- [x] Extract HTTP route dispatch (`TelemetryHttpRouter.cs`)
- [ ] Extract `CommandQueue.cs`
- [ ] Extract `SseHub.cs` (verify frame-version cache threading survives the move)
- [ ] Extract the MJPEG handler
- [ ] One-line SECURITY.md note once the method/size hardening ships (not a trust-model change, just
      documents that these two checks now exist)
