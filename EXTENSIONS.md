# Building a NOXMFD extension

NOXMFD can host MFD pages it didn't write. An **extension** is a separate BepInEx plugin — its
own `.dll`, its own repo, its own release cycle — that declares a dependency on NOXMFD and
registers itself at runtime. Once registered, its page shows up under NOXMFD's **EXT** nav
automatically: no fork of this repo, no pull request, no line of NOXMFD's own source changes.

The common case is simple: you want your own MFD page — your own instruments, your own content,
your own controls — reachable from the same display a pilot already has open, styled to match it.
Nothing about your page needs to be aware of anything NOXMFD itself talks to; it just needs to
register, serve some HTML, and optionally publish data or receive commands.

This document is the manual for writing one. It's aimed at a third-party modder who has never
seen NOXMFD's internals — everything you need is the public surface described here.

## Contents

- [Prerequisites](#prerequisites)
- [Quick start](#quick-start)
- [The five surfaces](#the-five-surfaces)
  - [1. Registering your extension](#1-registering-your-extension)
  - [2. Serving your page](#2-serving-your-page)
  - [3. Publishing telemetry](#3-publishing-telemetry)
  - [4. Receiving commands](#4-receiving-commands)
  - [5. A continuous video feed](#5-a-continuous-video-feed)
- [Appearing in the EXT nav — automatic](#appearing-in-the-ext-nav--automatic)
- [Reusing NOXMFD's shared assets](#reusing-noxmfds-shared-assets)
- [Versioning](#versioning)
- [What's not supported yet](#whats-not-supported-yet)
- [Troubleshooting](#troubleshooting)

## Prerequisites

- A BepInEx 5 plugin project of your own (its own `.csproj`, its own `[BepInPlugin]`).
- A copy of `NOXMFD.dll` to build against — either reference NOXMFD's source directly if you
  have a checkout of this repo, or (the normal case for a modder who doesn't) drop a built
  `NOXMFD.dll` into a `lib/` folder in your project and reference it as a plain assembly:

  ```xml
  <ItemGroup>
    <Reference Include="NOXMFD">
      <HintPath>lib\NOXMFD.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  ```

  `Private=false` matters: NOXMFD is already loaded by BepInEx as its own plugin, so your project
  must not also copy a second `NOXMFD.dll` into `BepInEx/plugins/` — that double-loads the
  assembly. Grab `NOXMFD.dll` from a NOXMFD release, or build it yourself from this repo.
- NOXMFD itself installed in the game you're testing against (see this repo's own `README.md`
  for install steps) — your extension declares a dependency on it and won't load without it.

## Quick start

The minimal extension: register in `Awake()`, serve one HTML file.

```csharp
using BepInEx;
using UnityEngine;

[BepInPlugin("com.yourname.my-extension", "MY PAGE", "0.1.0")]
[BepInDependency("com.roque.NOXMFD")]
public class Plugin : BaseUnityPlugin
{
    internal const string ExtId = "my-extension";

    private void Awake()
    {
        bool ok = NOXMFD.Api.RegisterExtension(ExtId, "MY PAGE", Resolve);
        if (!ok) Logger.LogError("Failed to register — id already taken?");
    }

    private static byte[]? Resolve(string relPath)
    {
        if (string.IsNullOrEmpty(relPath))
            return System.Text.Encoding.UTF8.GetBytes(
                "<!DOCTYPE html><html><body>Hello from my extension</body></html>");
        return null; // anything else 404s until you add real assets — see below
    }
}
```

Build, drop the DLL into `BepInEx/plugins/`, restart the game. Your page appears under NOXMFD's
**EXT** nav, labeled "MY PAGE", reachable in both the classic bezel and F-35 layouts, in full
view and split panes — you didn't write any of that wiring yourself.

## The five surfaces

Everything an extension can do goes through `NOXMFD.Api` (`using NOXMFD;`), a static class with
five capabilities. You don't need all five — the quick-start example above only used the first.

### 1. Registering your extension

```csharp
public static bool RegisterExtension(string id, string label, AssetResolver resolve, CommandHandler? command = null)
```

Call once, from your plugin's `Awake()`. `id` becomes your route prefix (`/ext/<id>/...`), your
command endpoint (`/ext/<id>/command`, only if you pass a non-null `command`), and the `action`
your EXT nav entry dispatches to. `label` is that entry's display text. Returns `false` (and logs
a warning on NOXMFD's side) if `id` is already taken by another registered extension — pick
something specific to your mod, not something generic like `main` or `map`.

There's no `Unregister` you need to call yourself in normal operation — BepInEx doesn't
hot-reload plugins, so a running extension has no ordinary reason to unregister mid-session.
(`Api.UnregisterExtension(id)` exists for tests / a clean shutdown path if you need it.)

### 2. Serving your page

```csharp
public delegate byte[]? AssetResolver(string relPath);
```

The `resolve` function you pass to `RegisterExtension`. NOXMFD calls it once per request to
`/ext/<id>[/<relPath>]`:

- `relPath == ""` → your page's own HTML, served at `/ext/<id>` (and `/ext/<id>/`).
- Anything else → an asset under that path — CSS, JS, images — served at `/ext/<id>/<relPath>`.
- Return `null` for "not found" — NOXMFD turns that into an HTTP 404. It never inspects the
  bytes you return or requires a particular format.

NOXMFD infers `Content-Type` from `relPath`'s file extension the same way it does for its own
pages — you never need to set MIME types yourself.

The natural way to implement this is embedded resources — bake your `.html`/`.css`/`.js` into
the DLL and match on the embedded resource name's suffix:

```csharp
internal static class MyAssets
{
    private static readonly System.Reflection.Assembly _asm = typeof(MyAssets).Assembly;

    internal static byte[]? Resolve(string relPath)
    {
        string name = string.IsNullOrEmpty(relPath) ? "page.html" : relPath;
        string suffix = "." + ("web." + name).Replace('/', '.');
        foreach (string n in _asm.GetManifestResourceNames())
            if (n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                using (var s = _asm.GetManifestResourceStream(n))
                {
                    var ms = new System.IO.MemoryStream();
                    s!.CopyTo(ms);
                    return ms.ToArray();
                }
        return null;
    }
}
```

with an `<EmbeddedResource Include="web\**\*" />` in your `.csproj` (matching whatever folder you
keep your web assets in). This is exactly the pattern NOXMFD uses for its own pages.

**Your page's contract with the shell.** Both NOXMFD layouts (classic bezel, F-35) host your
page in an `<iframe>` and talk to it via `postMessage`, the same contract every first-party
NOXMFD page gets:

```js
window.addEventListener('message', function (e) {
  var m = e.data;
  if (!m || m.mfd !== true) return;

  if (m.type === 'ext') {
    // m.data is whatever you last passed to Api.PublishSlice (see surface #3) — parsed already.
  } else if (m.type === 'orient') {
    // m.orientation is 'portrait' or 'landscape' — app-wide, same as every other page gets.
  }
});
```

You never need to detect which layout is hosting you, or whether you're in a split pane — the
message shape is identical either way.

### 3. Publishing telemetry

```csharp
public static void PublishSlice(string id, string json)
```

Call this whenever your state changes (or on a timer — NOXMFD's own pages publish at 10 Hz).
`json` must already be a valid JSON value — NOXMFD never parses it, only splices it verbatim into
the outgoing frame under `"ext":{"<id>":<json>}`. Last write wins if you call it faster than the
frame goes out.

You don't need to write any client-side plumbing to get this to your page — NOXMFD's own
`telemetry-source.js` forwards every `ext.<id>` key generically, and both shells rename it to the
`{mfd:true, type:'ext', data:<payload>}` message your page receives (shown above). This is fully
automatic once you call `PublishSlice` with your `id`.

```csharp
NOXMFD.Api.PublishSlice(ExtId, "{\"speed\":420,\"heading\":90}");
```

There's a second, higher-rate mechanism —
`PublishEvent(string eventName, string json)`, delivered as its own SSE event
(`ext-<eventName>`) instead of riding the 10 Hz frame — for something that would feel laggy at
that rate. **No NOXMFD page currently subscribes to this automatically on the browser side**, so
if you use it you'll need to open your own `EventSource` listener for `ext-<eventName>` from your
page's own JS. Most extensions won't need this — publish on the normal slice unless you've
specifically hit a smoothness problem there.

### 4. Receiving commands

Pass a non-null `command` to `RegisterExtension` and NOXMFD opens `POST /ext/<id>/command` for
you automatically:

```csharp
public delegate void CommandHandler(string json);
```

Your handler receives the raw POST body, called on the Unity main thread (queued and drained
between frames — safe to touch game state from inside it). NOXMFD never inspects or validates the
body — the shape is entirely yours to define. A minimal handler:

```csharp
private static void HandleCommand(string json)
{
    var cmd = JsonUtility.FromJson<MyCommandEnvelope>(json);
    switch (cmd.cmd)
    {
        case "do-thing": DoThing(cmd.value); break;
    }
}

[System.Serializable]
internal class MyCommandEnvelope { public string cmd = ""; public float value; }
```

From your page's JS, POST straight to your own endpoint — don't reuse NOXMFD's own
`send-command.js`, it's hardcoded to NOXMFD's own command shape:

```js
function postCmd(cmd, args) {
  return fetch('/ext/my-extension/command', {
    method: 'POST',
    body: JSON.stringify(Object.assign({ cmd: cmd }, args || {})),
  });
}
```

### 5. A continuous video feed

If your page needs a live camera-style feed (not just periodic JSON), NOXMFD serves an MJPEG
stream for you at `GET /ext/<id>/feed.mjpg` once you start pushing frames:

```csharp
public static void PushMjpegFrame(string id, byte[] jpg)   // call from your own capture loop
public static void ClearMjpegFrame(string id)               // e.g. on shutdown / disengage
public static bool WantsMjpegFrames(string id)               // true only while someone's watching
```

`WantsMjpegFrames` matters for cost: it's only `true` while your page (or a pane running it) is
actually open somewhere, so gate your capture work on it — no point rendering a camera and
JPEG-encoding frames nobody's looking at.

```csharp
if (!NOXMFD.Api.WantsMjpegFrames(ExtId)) return;   // skip capture entirely
byte[] jpg = CaptureAndEncode();                    // your own render → JPEG pipeline
NOXMFD.Api.PushMjpegFrame(ExtId, jpg);
```

On your page, point an `<img>` straight at the stream:

```html
<img src="/ext/my-extension/feed.mjpg">
```

## Appearing in the EXT nav — automatic

Once `RegisterExtension` succeeds, your `id`/`label` show up in `GET /ext-manifest`, which
NOXMFD's shell fetches once at boot and folds into the **EXT** nav — your label appears as a nav
item alongside every other installed extension, and clicking it navigates to your page. You do
nothing extra to make this happen; it's a consequence of registering, not a separate step.

EXT always lands on a hub page first — even with only your extension installed, a pilot presses
EXT and sees a small menu (MAIN + your label), not a direct jump into your page. That's
deliberate: it makes the EXT section legible as "a place multiple extensions can live," not a
special case for whichever one happens to be installed.

## Reusing NOXMFD's shared assets

Your page is served from the same origin as everything else, so it can reference NOXMFD's own
shared CSS exactly like a first-party page does — no CORS story, no copying files:

```html
<link rel="stylesheet" href="/assets/shared/font.css">
<link rel="stylesheet" href="/assets/shared/theme.css">
```

`theme.css` carries NOXMFD's color tokens (`var(--no-*)`) and a few reusable component classes —
`.mfd-empty`/`.mfd-empty-title` (a centered placeholder message, useful for an empty/not-ready
state) among them. Using these keeps your page visually consistent with the rest of the MFD
without hand-matching colors.

## Versioning

```csharp
public const int ApiVersion = 1;
```

`NOXMFD.Api.ApiVersion` is there if you want to branch on it at runtime, but the real enforcement
mechanism is BepInEx's own dependency check:

```csharp
[BepInDependency("com.roque.NOXMFD", MinimumVersion = "0.23.0")]
```

Pin a minimum NOXMFD version once your extension depends on behavior introduced at some specific
release. BepInEx then refuses to load your extension at all against an older NOXMFD, rather than
your code half-working against a shape that moved out from under it.

## What's not supported yet

- **`PublishEvent`'s browser side.** The C# emission mechanism works, but no NOXMFD page
  subscribes to `ext-<name>` events automatically — you'd need to open your own `EventSource`
  from your page's JS today. Publish on the normal 10 Hz slice (`PublishSlice`) unless you've hit
  a specific smoothness problem that needs a higher rate.
- **Jumping directly between two installed extensions.** Every extension's own nav currently
  offers only a MAIN back-link — hopping from one extension's page to another's costs a trip
  through MAIN (and then EXT) in between. Fine with a handful of extensions installed, a rough
  edge past that.
- **A clean "mission just ended" signal for your own slice.** NOXMFD's own built-in slices get
  explicitly cleared when a mission ends; your `PublishSlice`d data doesn't get an equivalent
  automatic reset (NOXMFD's web shell has no static knowledge of which extension ids exist). If
  your page needs to detect "no mission," derive it from some other top-level field it already
  receives rather than assuming your own slice gets cleared for you.

## Troubleshooting

- **My extension doesn't appear under EXT at all.** Check BepInEx's log for your plugin loading —
  a missing/too-old NOXMFD dependency, or an exception in `Awake()` before your
  `RegisterExtension` call, are the most common causes. `RegisterExtension` returning `false`
  (logged on NOXMFD's side) means your chosen `id` collided with another installed extension.
- **My page loads but shows nothing / 404s on its own assets.** Your `AssetResolver` is probably
  not matching the `relPath` your HTML's own `<link>`/`<script>` tags request — check the exact
  paths you're serving under `/ext/<id>/...` against what your resolver's suffix match expects.
- **I don't see my published telemetry in my page.** Confirm you're calling `PublishSlice` with
  the *exact same* `id` you registered with — a typo between the two means your data goes to a
  key nothing reads. Also confirm your page's `message` listener checks `m.mfd === true` and
  `m.type === 'ext'` before reading `m.data`, as shown above.
