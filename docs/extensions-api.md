# Extension API — letting other mods add MFD pages without touching NOXMFD source

## Status

**Built (`extensions-api`, off `feature/rc-missile-camera`) and now proven by a real
extension** (`rc-as-extension`, off `extensions-api`): `extensions/rc-missile-camera/` is
RC rewritten as a genuinely separate BepInEx plugin, replacing PR #45's in-source version.
Five surfaces now have a working implementation — page serving, telemetry publishing (the
normal 10 Hz slice; the high-rate SSE-event variant exists server-side but nothing
subscribes to it yet, see Deferred), command registration, the EXT nav fold, and a fifth
added specifically because RC needed it: a continuous MJPEG video feed
(`Api.PushMjpegFrame`/`WantsMjpegFrames`, served at `/ext/<id>/feed.mjpg`) — RC's `/rc.mjpg`
was a long-lived streaming response, not a static asset, which none of the original four
surfaces could serve. Not yet verified in-game (no MissileCamera/MissileCameraRemoteControl
install available to exercise the real feed against) — both projects build clean and the
full web test suite passes, but that's static verification only.

## Motivating case

[PR #45](https://github.com/roke77/NOXMFD/pull/45) — lupfine's RC (MissileCamera: Remote
Control) page — is the concrete example that started this conversation. To land it, his
work had to be re-applied *inside* NOXMFD's own source: new files in `src/plugin/`, three
new fields spliced into `CommandEnvelope`, a hand-edited block in the telemetry JSON
builder, and hand-edits across `nav-model.js` / `layout-pages.js` / `split-slots.js` /
`mfd.js` / `f35.js` / `telemetry-source.js` — eleven files, none of which lupfine could
have written himself without a working copy of this repo. See
`_scratch/lupfine-review/NOISE.md` for what a modder editing NOXMFD's own source costs
when their fork's base has drifted: half the diff was other people's regressions, not his
feature.

**The goal this doc is about:** a modder should be able to write RC-the-MissileCamera-page
as its *own* mod — its own `.dll`, its own repo, its own release cycle — that declares
NOXMFD as a BepInEx dependency and adds itself to the running MFD without a single line
changing in this repo. NOXMFD becomes a *platform* other mods build on, the same
relationship NOXMFD itself already has with MissileCamera (McBridge.cs) and
MissileCameraRemoteControl (RcBridge.cs) — just pointed the other direction.

## The two integration patterns already in this codebase, and why only one fits

NOXMFD already talks to two other mods, and the pattern it uses is worth naming precisely
because it's the *wrong* one for this problem:

- **Reflection soft-dependency** (`McBridge.cs`, `RcBridge.cs`). No compile-time reference
  either direction; the consumer scans `AppDomain.CurrentDomain.GetAssemblies()`, resolves
  a type by string name, binds each member to a cached delegate via
  `Delegate.CreateDelegate`, retries on a cooldown since BepInEx load order between two
  independent mods isn't guaranteed. This exists because MissileCamera has never heard of
  NOXMFD and never will — a hard reference would mean NOXMFD requiring a mod most players
  don't have.
- **Hard BepInEx dependency** (`[BepInDependency(GUID, MinimumVersion)]`), a real
  `<ProjectReference>` or DLL reference, plain C# method calls. BepInEx refuses to load the
  dependent plugin at all if the dependency is missing or below the pinned version — the
  failure is "extension mod doesn't appear," not a null-delegate crash at an arbitrary call
  site.

An extension mod *wants* to depend on NOXMFD — that's the premise. So it uses the second
pattern: NOXMFD publishes a versioned public API surface (mirroring the `ApiVersion` const /
`MinApiVersion` check idiom `McBridge.cs`/`RcBridge.cs` already use, just from the producer's
side this time), extensions pin a minimum version via `BepInDependency`, and a stale
extension simply fails to load rather than half-working against a shape that moved under it.

## The four surfaces — what's built

### 1. Page serving — **built**

`src/plugin/Api.cs` (`RegisterExtension`) + `src/plugin/ExtensionRegistry.cs` (the backing
store). An extension calls `Api.RegisterExtension(id, label, resolve, command)` once from its
own `Awake()`; `resolve` is a `Func<string relPath, byte[]?>` — `""` means the page's own
HTML, anything else an asset under it. `TelemetryServer.HandleExtRequest` routes
`/ext/<id>[/<relPath>]` generically to whichever extension's `resolve` matches, inferring
Content-Type from `relPath`'s extension the same way `ServeAssetRel` does for NOXMFD's own
pages — an extension never needs to know MIME types. Same origin as everything else (no CORS
story): an extension's page can `<script src="/assets/services/send-command.js">` and
`<link href="/assets/shared/theme.css">` exactly like a first-party page (open question below:
this hasn't been proven against an actual external `.dll` yet, only reasoned about).

`GET /ext-manifest` lists every registered extension as `[{id, label}, ...]`, sorted by id —
the one thing the web side needs to discover them (surface #4).

### 2. Telemetry publishing — **built**

`Api.PublishSlice(id, json)` — last-write-wins, spliced into the outgoing 10 Hz frame under
`"ext":{"<id>":<json>}` (`ExtensionRegistry.SlicesJson`, appended in
`TelemetryServer.Serialize`). Client-side, `telemetry-source.js`'s `_emit()` forwards every
`d.ext` key generically as a `{type:'ext_<id>', data:<payload>}` message — no per-extension
code on the web side at all. Both shells then rename that to the page-facing contract: an
extension's own iframe always receives `{mfd:true, type:'ext', data:<payload>}` regardless of
layout (classic bezel: `forwardExtToPanes`/`forwardExtToFrame` in `mfd.js`; F-35:
`forwardSlice`'s `ext_` rename in `f35.js`) — the same "write your page once, it renders
identically under either shell" guarantee every first-party page gets.

`Api.PublishEvent(eventName, json)` also exists for a continuous value that would feel laggy
at 10 Hz — `TelemetryServer`'s SSE loop diffs a runtime-registered set of event names per
connection, the same change-gating `cursor` already does, and ships each as its own
`event: ext-<name>` — the `ext-` prefix is added server-side so an extension can't collide
with a built-in event name. **No browser page currently subscribes to this automatically** —
see Deferred. RC's own aim reticle, the reason this got built in the first place, ended up
*not* using it (see `RcTelemetry.cs`'s ponytail note): it rides the normal 10 Hz slice
instead, since the browser-side half of this surface is exactly what's deferred.

### 3. Command registration — **built**

`POST /ext/<id>/command` — the raw body is queued (`ExtensionRegistry`'s own bounded queue,
same 64-item cap and lock shape as `TelemetryServer`'s `/command` queue) and drained on the
Unity main thread by `ExtensionRegistry.Drain()` (called from `MissionLifecycle.Update`,
alongside `CommandDispatcher.Drain()` — an extension's command endpoint works at the main
menu too, same reasoning as `/keybinds`). The registered `Api.CommandHandler` gets the raw
JSON string and parses it against whatever envelope shape the extension itself defines.
`CommandEnvelope` never grows for extension needs — it stays exactly what it is today, one
shared shape for NOXMFD's own commands.

### 4. Nav registration — **built**

A dedicated **EXT** entry in `NAV.main` (`nav-model.js`), landing on whichever installed
extension's id sorts first — the same "one fold, one default landing page" shape
`AKF`/`MIS`/`OBJ`/`BDF`/`PAL` already use under MDT (`akf` is MDT's hardcoded default; here
the default is *discovered*, not authored). `NAV.ext`'s static baseline is just the MAIN
back-link; `src/web/shell/ext-nav.js` fetches `/ext-manifest` once at boot and appends one
sub-item per installed extension, plus a matching `NAV[<id>]` (also just a MAIN back-link —
see the ponytail note below) so that extension's own page renders nav labels the same way any
other frame-hosted page does. This is the one NAV entry whose *contents*, not just its
presence, are runtime-discovered rather than hand-authored and test-pinned — `ext-nav.test.js`
covers the pure merge logic (`buildExtNavPlan`); `nav-model.test.js`/`layout-coverage.test.js`/
`split-slots.test.js` were updated to treat EXT's static baseline like any other
MAIN-back-link-only page and classify the `'ext'` action itself as dispatched-specially
(mirrors `'lyt'`), never table-resolved.

Page-URL resolution generalizes with a fallback rather than a per-extension table row: both
shells' `FRAME_PAGES`/`PAGE_URL`/`F35_PAGES` lookups fall back to `/ext/<id>` (`?bare` for
split) whenever the name isn't a static key but *is* a live extension id
(`ExtNav.isExtensionPage`). F-35's `has()` folding that check in is what makes an extension id
a valid `showPage`/`dispatch`/`canDo` target everywhere those already gate on it — one change,
not three, thanks to that layout's more uniform architecture (see `docs/layouts.md`'s own
"the seam" section for why).

**ponytail:** every extension's `NAV[<id>]` is just `[{label:'MAIN',action:'main'}]` today,
not the full N-way sibling swap NAV.akf's group gives MIS/OBJ/BDF/PAL — jumping between two
installed extensions costs a trip through MAIN. Ceiling: fine for a handful of extensions,
gets old past that. Upgrade path: give each `NAV[<id>]` the same sibling list `NAV.ext`
carries (minus itself), mirroring the AKF fold exactly — noted in `ext-nav.js` at the
`buildExtNavPlan` call site.

### 5. MJPEG feed — **built (added when RC needed it)**

Not in the original four surfaces — found missing when RC was actually converted. RC's video
feed (`/rc.mjpg` in PR #45) is a long-lived `multipart/x-mixed-replace` streaming response;
`RegisterExtension`'s asset `resolve` returns one `byte[]` per request and has no way to serve
that. `Api.PushMjpegFrame(id, jpg)` / `ClearMjpegFrame(id)` / `WantsMjpegFrames(id)`, backed by
a generic per-id buffer in `ExtensionRegistry` (same shape as `TelemetryServer`'s own
`_tgpJpg`/`_tgpFrameId`/`_tgpLock`/`_tgpSubscribers`, just keyed instead of hardcoded), served
at `GET /ext/<id>/feed.mjpg` by a generic `HandleExtMjpegAsync` mirroring `HandleMjpegAsync`.
`RcFeed.cs` — moved into `extensions/rc-missile-camera/` unchanged in shape, just repointed
from `TelemetryServer.WantsRcFrames`/`PushRcFrame`/`ClearRcFrame` to the `Api` equivalents — is
the proof this surface works.

## Versioning

`Api.ApiVersion` (currently `1`) is the constant an extension can branch on at runtime; the
enforcement mechanism is `[BepInDependency("com.roque.NOXMFD", MinimumVersion = "X.Y.Z")]` on
the extension's own plugin class, which makes BepInEx itself refuse to load a stale extension
rather than NOXMFD detecting and degrading at runtime the way `McBridge`/`RcBridge` do for
mods that don't know they're a dependency. `rc-missile-camera`'s own `[BepInDependency]` doesn't
pin a `MinimumVersion` yet (see its `Plugin.cs`) — both projects still move together on the same
branch, so there's nothing to pin against yet.

## Deferred (scoped out of this pass, not solved)

- **Browser-side wiring for `Api.PublishEvent`.** The C# emission mechanism (SSE loop, per-
  connection diffing, `ext-<name>` event) is built and reachable, but no shell or page
  subscribes to it — `EventSource.addEventListener` needs to know an event name up front, so
  making this fully generic requires either the manifest declaring which event names an
  extension publishes, or every registered extension getting one conventionally-named
  `ext-<id>` listener whether it uses it or not. Still punted: RC's aim reticle — the
  motivating consumer — ended up riding the normal 10 Hz slice instead (`RcTelemetry.cs`'s
  ponytail note) rather than forcing this design question through under time pressure.
- **`_emitEmpties()` doesn't clear ext slices on mission end.** `telemetry-source.js` has no
  static knowledge of which extension ids exist (only `/ext-manifest`, fetched by
  `ext-nav.js`, knows that), so a mission-end reset can't emit specific
  `{type:'ext_<id>', ...}` clears the way it does for every built-in slice. An extension's
  page may show stale data for a beat after a mission ends unless it derives "no mission" from
  some other top-level frame field itself. Minor, not a blocker.
- **Multiple extensions, id collisions — resolved, not deferred:** `ExtensionRegistry.Register`
  rejects a duplicate `id` with a logged warning and returns `false` (mirrors `RcBridge`'s
  "shape mismatch — disabled" degrade-not-crash posture). Listed here only so it's clear this
  question *was* answered, unlike the two above.

## Still open

- **`theme.css`/`font.css` as-is — confirmed, not just reasoned.** `rc.html` references
  `/assets/shared/{font,theme}.css` unchanged and it works: same origin, same paths every
  first-party page already uses. **`send-command.js` — deliberately NOT reused.** It's
  hardcoded to POST `/command` against `CommandEnvelope`'s shape; rather than extend NOXMFD's
  shared file for one extension's sake, `rc.js` posts to its own
  `/ext/rc-missile-camera/command` with its own tiny inline helper (`postCmd`). Worth revisiting
  if a second extension wants the same thing — a shared `ext-command.js` taking an endpoint
  parameter might earn its keep at that point, but one caller doesn't justify it yet.
- **Does an extension get its own `docs/` entry in this repo, or does that live in the
  extension's own repo entirely?** Leaning: entirely theirs — NOXMFD's docs describe NOXMFD,
  not what's built on it, same as this repo doesn't document what MissileCamera itself does.
  `extensions/rc-missile-camera/` has none of its own beyond code comments, consistent with
  that leaning.

## Related

- `extensions/rc-missile-camera/` — the real, working proof: RC rewritten as a genuinely
  separate BepInEx plugin, calling only `NOXMFD.Api`. Its own `Plugin.cs`/`RcLifecycle.cs`
  mirror NOXMFD's own `Plugin.cs`/`MissionLifecycle.cs` boot pattern (a self-spawned,
  `DontDestroyOnLoad` GameObject — the Plugin GameObject itself doesn't survive the
  boot → MainMenu scene transition in this Unity version).
- [PR #45](https://github.com/roke77/NOXMFD/pull/45) — the RC page's *original*, in-source
  version, kept as the historical before/after: what `extensions/rc-missile-camera/` replaced.
- `_scratch/lupfine-review/NOISE.md` — what happens when a modder's fork drifts from a
  moving base; a real API surface sidesteps this entirely (an extension mod has no "base
  commit" to drift from — it only ever calls a versioned public API).
- `docs/src-architecture.md` — the asset-serving convention (`ServeAssetRel`, embedded
  resources) surface #1 generalizes past "this one assembly's manifest."
- `docs/layouts.md` — the `NAV`/layout-renderer seam, and the single-`/stream`-owner
  invariant surface #2 respects.
- `extensions/rc-missile-camera/McBridge.cs`, `RcBridge.cs` — the reflection soft-dependency
  pattern, kept here as the *contrast* case: correct for an extension depending on a mod that
  has never heard of NOXMFD, wrong for the extension's own dependency on NOXMFD itself (which
  uses the hard `[BepInDependency]` instead — see Versioning above).
- `src/plugin/Api.cs`, `src/plugin/ExtensionRegistry.cs` — the public surface and its backing
  store. `src/web/shell/ext-nav.js` — the web-side nav discovery. Every other touch point is
  a small fallback added to existing machinery (`TelemetryServer.cs`'s route table and frame
  serializer, `MissionLifecycle.cs`'s drain call, `telemetry-source.js`'s `_emit()`, both
  shells' page-URL/nav-dispatch lookups) rather than new machinery of its own.
