# Error-handling improvements

## Purpose

Improve diagnostics and failure containment in `src/plugin/` without changing normal
gameplay, browser, or extension behavior. The review covers all 78 C# source files in
the plugin runtime, with emphasis on HTTP/background work, Unity reflection, GPU
capture, persistence, Steam transport, and extension boundaries.

## Current strengths

- Command bodies and queues are bounded, and command execution returns to the Unity
  main thread.
- `TelemetryServer` tracks active HTTP handlers, aborts active responses during
  shutdown, waits with bounded timeouts, and probes for port release.
- Persistent stores recover from unreadable data by keeping a usable empty state.
- SSE and MJPEG handlers use cancellation and release subscriptions in `finally`.
- Several game-reflection reads already degrade to safe display defaults.

## Implemented improvements

### 1. Make short HTTP endpoint failures observable

**Priority: high**

`ConfigEndpoint` and parts of `CapturedAssetEndpoint` broadly swallow exceptions.
That is appropriate only for expected response-close failures after a browser has
disconnected. It currently also hides serialization, configuration, and local-file
errors, leaving a browser with a partial or closed response and no server diagnosis.

Completed:

- Add a narrowly scoped shared response-writing/error-classification helper in the
  HTTP area.
- Treat cancellation and known client-disconnect response failures as expected.
- Log unexpected failures with endpoint/path context and the exception object.
- Return HTTP 500 when the response has not already started.
- Keep `Response.Close()` cleanup best-effort in `finally`.

Primary files:

- `src/plugin/Http/ConfigEndpoint.cs`
- `src/plugin/Http/CapturedAssetEndpoint.cs`
- `src/plugin/Http/TelemetryAssets.cs`

### 2. Differentiate MJPEG disconnects from server defects

**Priority: high**

`TgpMjpegHandler` and extension MJPEG handling classify every non-cancellation
exception as a normal client disconnect. This hides defects in frame retrieval,
response setup, encoding, or extension code.

Completed:

- Keep silent cancellation handling.
- Recognize expected broken-pipe/response-close failures as disconnects.
- Log unexpected exception types with endpoint, client, and extension-id context.
- Preserve subscription decrement/unsubscribe and response cleanup in `finally`.

Primary files:

- `src/plugin/Http/TgpMjpegHandler.cs`
- `src/plugin/Http/ExtensionEndpoint.cs`

### 3. Contain game-reflection failures at capability boundaries

**Priority: high**

Some reflection adapters safely fall back, but other private field/method access can
escape from a Unity callback or frame update. A game update, scene teardown, or a
Unity fake-null edge case could disable unrelated work in that frame and leave little
actionable evidence.

Completed:

- Add narrowly named `Try...` operations to each affected game-specific adapter; do
  not introduce a generic reflection utility bucket.
- Catch reflection invocation/read/write failures at the adapter boundary.
- Log one rate-limited capability warning with the member name and exception.
- Disable or retry only the affected capability, and clear stale TGP/HUD state when
  needed.
- Avoid a broad catch around `MissionLifecycle.Update`, which would obscure which
  subsystem failed and risks masking unrelated defects.

Primary files:

- `src/plugin/Tgp/TgpFeed.cs`
- `src/plugin/Tgp/TgpManualTargetCamAccess.cs`
- `src/plugin/Assets/AssetCapture.cs`
- `src/plugin/Hud/CombatHudMarkerLookup.cs`
- `src/plugin/Hud/HudTtiCue.cs`
- `src/plugin/Hud/MissileSeekerAccess.cs`

### 4. Make sprite capture exception-safe

**Priority: high**

The synchronous `SpriteCapture` path restores `RenderTexture.active` only after all
pixel operations succeed. An exception can leave Unity's global active render target
changed and can leave its temporary `Texture2D` undisposed. Its background delivery
callback also runs directly, so a consumer exception can become an unobserved task
failure.

Completed:

- Restore `RenderTexture.active` in a nested `finally`.
- Destroy the temporary `Texture2D` in `finally`.
- Wrap delivery callbacks in a dedicated safe delivery method that logs consumer
  failures and preserves the `null` failure signal.
- Keep GPU readback and JPEG/PNG encoding off the main thread as they are now.

Primary file:

- `src/plugin/Assets/SpriteCapture.cs`

### 5. Diagnose wildcard bind failures correctly

**Priority: medium**

`TelemetryServer.TryBindWildcard` treats every `HttpListenerException` like a missing
URL reservation. A port conflict can therefore trigger unnecessary LAN auto-setup and
be reported as a generic localhost fallback.

Completed:

- Distinguish access/URL-reservation failures from address-in-use and other listener
  failures using the `HttpListenerException` error code.
- Log the actual reason before attempting auto-setup or localhost fallback.
- Keep the existing safe fallback behavior.

Primary file:

- `src/plugin/Http/TelemetryServer.cs`

### 6. Preserve best-effort backup behavior while reporting lost protection

**Priority: medium**

`ConfigBackup` intentionally does not prevent a save when copying the prior file to
`.bak` fails. This is the right availability choice, but it leaves users unaware that
the recovery copy is unavailable.

Completed:

- Retain best-effort writes.
- Add a rate-limited warning containing the affected path and failure reason.
- Do not turn backup failure into a save failure.

Primary file:

- `src/plugin/ConfigBackup.cs`

### 7. Keep malformed extension payloads out of shared SSE JSON

**Priority: medium**

Extension slices and events are accepted as arbitrary strings and slices are inserted
verbatim into the shared telemetry JSON. A malformed payload can invalidate a frame
for every connected browser.

Completed:

- Validate published JSON at the extension registry boundary.
- Reject malformed input with a diagnostic naming the extension/event.
- Retain the last known-valid value or omit the invalid entry.
- Keep extension payloads opaque after they pass validation.

Primary file:

- `src/plugin/Extensions/ExtensionRegistry.cs`

### 8. Lifecycle cleanup and diagnostic polish

**Priority: low**

Completed:

- Dispose cancelled `CancellationTokenSource` instances after request handlers and the
  accept thread have drained.
- Record `netsh` process timeout/output details and clean up a timed-out child process
  safely.
- For reflection-backed telemetry values that fall back to zero/false/default color,
  add one-time capability diagnostics so game API drift is not indistinguishable from
  genuine data values.

Primary files:

- `src/plugin/Http/TelemetryServer.cs`
- `src/plugin/Telemetry/TelemetryReader.cs`
- `src/plugin/Squad/Squadron.cs`

## Completion order

1. Short HTTP endpoint observability.
2. MJPEG exception classification.
3. TGP and reflection capability containment.
4. `SpriteCapture` cleanup and callback containment.
5. Listener bind diagnostics, backup diagnostics, extension payload validation, and
   lifecycle polish.

## Verification completed

- Added BepInEx/Unity-free validation tests for extension JSON payload syntax.
- Ran the full local CI smoke suite, including the Release build, web tests, and C# tests.

## Live-game validation still required

- Exercise TGP capture and manual controls, HUD cues, and reflection fallbacks across
  a normal spawn, respawn, and scene transition.
- Keep an SSE or MJPEG client connected while stopping the server. Confirm active
  requests are logged, unwind within the configured bound, and the port-release
  diagnostic accurately reports the result.
- Verify an invalid extension slice/event produces one diagnostic, does not corrupt
  the SSE frame, and leaves the previous valid value available.
