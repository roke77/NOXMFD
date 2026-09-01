# Extension API

## Status

**Built and available as API version 1.** A separate BepInEx plugin can register an MFD page,
publish telemetry, receive commands on the Unity main thread, appear under the EXT navigation hub,
and provide an MJPEG feed without changing NOXMFD source.

The concrete example is
[NOXMFD-Extension-Remote-Control-Missile-Camera-POC](https://github.com/roke77/NOXMFD-Extension-Remote-Control-Missile-Camera-POC),
which converts the MissileCamera remote-control page proposed in
[PR #45](https://github.com/roke77/NOXMFD/pull/45) into a separate plugin that references a built
`NOXMFD.dll`. The example validates registration, navigation, page serving, and its no-camera-plugin
placeholder. Its live camera integration still requires the corresponding camera plugins and is
tracked by that extension rather than this repository.

NOXMFD does not contain integration-specific bridge classes. Extensions depend on NOXMFD and call
the public `NOXMFD.Api` surface directly.

## Dependency and lifecycle

An extension references a built `NOXMFD.dll` and declares NOXMFD as a BepInEx dependency:

```csharp
[BepInDependency("com.roque.NOXMFD", MinimumVersion = "<required NOXMFD version>")]
```

The extension normally calls `Api.RegisterExtension` from its `Awake()` method and
`Api.UnregisterExtension` during teardown. Use a stable, URL-safe id because it becomes part of the
page, command, feed, telemetry, and navigation contracts.

Threading requirements differ by callback:

- Registration, unregistration, and ordinary publishing normally run on the Unity main thread.
- The asset resolver runs on an HTTP worker and must not touch Unity or other main-thread-only game
  state.
- The command handler always runs on the Unity main thread after its request has been validated and
  queued.

`Api.ApiVersion` is currently `1`. Breaking public-API changes require incrementing it. Runtime
compatibility is informational; BepInEx's `MinimumVersion` is the load-time enforcement mechanism.

## 1. Page and asset serving

```csharp
bool registered = Api.RegisterExtension(
    id,
    label,
    ResolveAsset,
    HandleCommand);
```

`RegisterExtension` returns `false` for an empty id, a null resolver, or an already registered id.
An empty label falls back to the id.

The resolver receives paths relative to the extension root:

- `""` serves `GET /ext/<id>` and normally returns the page HTML.
- A non-empty path serves `GET /ext/<id>/<relative-path>`.
- `null` produces a 404.

`ExtensionEndpoint` infers the content type from the path through the same mapping used by built-in
assets. Responses are same-origin, so extension pages can reuse resources such as
`/assets/shared/theme.css` and `/assets/shared/font.css`.

`GET /ext-manifest` returns registered entries sorted by id:

```json
[{"id":"example","label":"EXAMPLE"}]
```

## 2. Telemetry publishing

`Api.PublishSlice(id, json)` stores the latest JSON value for an id. `TelemetryJson` inserts the
current values into the normal telemetry frame:

```json
{"ext":{"example":{"value":42}}}
```

The browser telemetry service emits this as `ext_<id>`. Both shells forward the active extension's
slice into its iframe using the stable page-facing message:

```js
{ mfd: true, type: 'ext', data: payload }
```

Published JSON is trusted and appended without parsing or validation, so the extension must provide
valid JSON.

`Api.PublishEvent(eventName, json)` stores a latest-value, change-gated SSE event. `SseHub` sends it
as `event: ext-<eventName>`. The server side exists, but the shells do not automatically subscribe
to runtime-discovered event names. An extension that needs this channel must currently arrange its
own browser listener; use `PublishSlice` for the portable default.

## 3. Commands

If registration includes a command handler, the page can send:

```text
POST /ext/<id>/command
Content-Type: application/json
```

`CommandEndpoint` requires POST and a JSON content type, limits the body to 16 KiB, and queues at
most 64 pending extension commands. It returns an accepted response only after enqueueing succeeds.
`MissionLifecycle.Update()` drains the queue every frame, including at the main menu, and invokes
the registered handler with the raw JSON string on the Unity main thread. Handler exceptions are
logged without stopping the queue.

Extension commands deliberately do not use NOXMFD's internal `CommandEnvelope`; each extension owns
and parses its command schema.

## 4. Navigation

Both layouts contain a built-in EXT hub. At startup, `ext-nav.js` fetches `/ext-manifest`, appends
one navigation item per registered extension, and records each id as a runtime page destination.
The classic and F-35 shells resolve those destinations through `/ext/<id>` fallbacks rather than
static per-extension page tables.

EXT always opens the hub first. Selecting an installed extension opens its registered page. An
extension page currently has a MAIN return item; switching directly between extensions requires a
trip through MAIN and EXT.

The merge and layout behavior is covered by `ext-nav.test.js`, `nav-model.test.js`,
`layout-coverage.test.js`, and `split-slots.test.js`.

## 5. MJPEG feed

An extension can publish a continuous JPEG stream:

```csharp
if (Api.WantsMjpegFrames(id))
    Api.PushMjpegFrame(id, jpgBytes);
```

`GET /ext/<id>/feed.mjpg` serves the latest frames as
`multipart/x-mixed-replace`. `WantsMjpegFrames` reports whether at least one client is subscribed,
allowing the producer to skip capture and encoding work while unused. `ClearMjpegFrame` removes the
current frame.

The registry keeps independent frame, sequence, lock, and subscriber state for each id. The HTTP
handler polls for a changed frame every 30 ms; the extension controls the actual publish cadence.

## Known limitations

- The generic browser side does not subscribe to names published through `Api.PublishEvent`.
- Mission-end `_emitEmpties()` cannot emit id-specific clears because the telemetry service does not
  own the runtime extension manifest. An extension page should also derive mission availability from
  normal frame state when stale data matters.
- Published telemetry and SSE strings are not JSON-validated by NOXMFD.
- Extension ids are not normalized or escaped for use as route segments; extensions must choose
  stable URL-safe ids.
- The API does not provide a generic browser command helper. An extension posts to its own endpoint
  directly because the built-in `send-command.js` targets NOXMFD's internal `/command` envelope.

## Implementation map

- `src/plugin/Extensions/Api.cs` — public API contract.
- `src/plugin/Extensions/ExtensionRegistry.cs` — registrations, telemetry values, MJPEG state, and command
  queue.
- `src/plugin/Http/ExtensionEndpoint.cs` — manifest, extension assets, commands, and MJPEG routing.
- `src/plugin/Http/CommandEndpoint.cs` — request validation and bounded command-body handling.
- `src/plugin/Http/SseHub.cs` and `src/plugin/Telemetry/TelemetryJson.cs` — extension event and slice
  serialization.
- `src/web/shell/shared/ext-nav.js` and `src/web/pages/ext/ext.html` — runtime discovery and EXT hub.
- `src/web/services/telemetry-source.js` plus the classic and F-35 shells — extension telemetry
  forwarding.
