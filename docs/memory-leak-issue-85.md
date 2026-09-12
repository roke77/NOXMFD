# Browser memory growth — issue #85

## Status

Investigation and implementation plan for [issue #85](https://github.com/roke77/NOXMFD/issues/85).

The first lifecycle hardening pass shipped in 0.48.1: MAP closes its `EventSource` and watchdog on
`pagehide`, TGP aborts its MJPEG request and retry timer on `pagehide`, the classic shell unloads
resource-owning frame pages when they become hidden, and F-35 portal navigation/destroy paths cancel
pending hold/repeat timers. Those changes address real leaks, but they do not yet explain or fix all
reported browser-memory growth.

The strongest remaining reproduction is repeated **MAP -> WPT -> MAP** navigation. In F-35 portals
and classic split panes, every return to MAP creates a new MAP document and requests the same large
map under a unique timestamped URL. That image-resource churn is now the leading cause for the large
stepwise RAM increase seen during navigation. Continuous high-bandwidth TGP MJPEG playback remains a
separate long-session risk that needs controlled measurement after the navigation leak is fixed.

No implementation below is selected merely by appearing in this document. The preferred ordering is
to establish repeatable measurements, fix MAP image identity and teardown, then profile a single
visible TGP stream before changing its transport.

## User reports

Issue #85 reports that a Windows 10 LTSC browser tab can reach 7-9 GB after roughly 20-30 minutes,
with severe lag that persists after displays or features are closed. A complete refresh or browser
process termination releases the memory. The reporter could not reproduce it on Android.

A later local reproduction identified a faster trigger: moving between MAP and WPT repeatedly makes
browser RAM rise by a large amount.

These may share a browser-resource-lifetime cause, but they must be measured separately:

1. **Navigation case:** memory rises in steps when MAP is repeatedly recreated.
2. **Steady-stream case:** memory rises over time while one visible TGP MJPEG feed remains open.
3. **Hidden-resource case:** memory/work continues after a page is no longer visible.

## Current browser architecture

### Classic full view

The canonical `mapFrame` stays loaded for the lifetime of the shell and acts as both the full MAP
view and the telemetry tap. Frame-hosted pages such as WPT and TGP use `pageFrame` above it.

Consequently, full-view MAP -> WPT -> MAP should not recreate the MAP document. If this exact layout
still shows large per-transition growth, it is evidence against MAP reloads being the sole cause and
must be profiled independently.

### Classic split view

Each pane owns one iframe. `paneNavigate()` assigns a new URL whenever the pane changes page, so
MAP -> WPT -> MAP destroys and recreates the pane's MAP document.

### F-35 view

Each portal owns one iframe. `showPage()` assigns `frame.src` on every page selection, so MAP -> WPT
-> MAP also destroys and recreates the portal's MAP document. The F-35 shell additionally owns one
intentional hidden MAP telemetry tap. Multiple `/stream` connections with one client id can therefore
be legitimate: count connections against the number of intentional taps and visible MAP surfaces,
not against browser tabs alone.

## Findings and confidence

### F1 — timestamped MAP URLs create large, distinct browser resources

**Confidence: high for the mechanism; live memory attribution still required.**

On its first valid telemetry frame, every new MAP document runs:

```js
mapImg.src = '/map?t=' + Date.now();
```

The retry and mission-clear paths use the same timestamp pattern. Every visit is therefore a unique
HTTP cache key even though the server normally returns the same mission map bytes.

The plugin caps the captured map's longer side at 4096 pixels and encodes it as JPEG quality 85. JPEG
size is not the important browser-memory cost: a decoded 4096x4096 RGBA image is 67,108,864 bytes,
or 64 MiB. A renderer-side decoded surface plus a GPU texture can cost more. Ten recreated MAP pages
can therefore generate hundreds of MiB of image/texture churn even if JavaScript heap remains small.

The `/map` binary response currently provides no explicit cache policy or stable entity identity.
Timestamp query parameters prevent the browser from sharing a single resource entry across MAP
documents and can leave many decoded resources in its memory cache until pressure-based eviction.
This can look and feel like a leak even when the old DOM document itself is collectible.

Relevant code:

- `src/web/pages/map/map.js`: initial map load, retry, and mission-clear timestamp URLs;
- `src/plugin/Assets/AssetCapture.cs`: `MapMaxDim = 4096`, JPEG quality 85;
- `src/plugin/Http/CapturedAssetEndpoint.cs`: captured `/map` response;
- `src/plugin/Http/TelemetryServer.cs`: `WriteBinary`, currently without cache headers.

### F2 — MAP teardown releases the transport but not heavy render resources explicitly

**Confidence: medium.**

The 0.48.1 `pagehide` handler calls `TelemetrySource.disconnect()`, closing the `EventSource` and its
watchdog. It does not currently clear:

- the map image source;
- the overlay canvas backing store;
- the `ResizeObserver`;
- a pending map-image retry timer;
- the incoming-missile animation interval;
- a pending animation frame;
- decoded icon images and pre-tinted icon canvases.

Normal iframe navigation should eventually dispose of the document and those resources. Explicit
teardown is still valuable because it makes ownership deterministic and prevents asynchronous work
from extending the lifetime of a departing page. It will not, by itself, evict all uniquely keyed
images from the browser's shared image cache; F1 must be fixed as well.

Iframe navigation does add nested session-history entries, but current Chrome guidance says a page
navigated away inside an iframe does not independently enter the back/forward cache. BFCache is
therefore not the leading explanation for this reproduction. Replacement-style navigation remains
appropriate because MFD page selection is not intended to build browser history.

References:

- [HTML navigation and nested session history](https://html.spec.whatwg.org/dev/browsing-the-web.html)
- [Chrome/Web.dev back-forward cache and iframe behavior](https://web.dev/articles/bfcache)

### F3 — hidden live-resource pages existed before 0.48.1

**Confidence: high; addressed in 0.48.1.**

The classic shell previously hid `pageFrame` with CSS when moving from TGP to a non-frame page or
when entering split mode. The iframe and `/tgp.mjpg` request stayed alive. Entering split from TGP
could leave the full stream hidden while opening another visible TGP pane, and two visible panes
could coexist with a third hidden subscriber.

The current `STREAMING_FRAME_PAGES` convention unloads resource-owning full-frame pages when they
become hidden, and the TGP page explicitly aborts its stream on `pagehide`. Keep regression coverage
for this behavior; it directly matches the report that closing a feature did not reduce load.

### F4 — MAP `EventSource` and watchdog lacked explicit ownership

**Confidence: medium as a contributor; addressed in 0.48.1.**

Every MAP document owns an `EventSource('/stream')` and watchdog interval. Before 0.48.1 neither was
retained for explicit cleanup. The current `TelemetrySource.disconnect()` and MAP `pagehide` wiring
close both, with unit coverage for `disconnect()`.

This is correct lifecycle hardening, but an abandoned SSE connection alone does not explain the
large MAP-specific increments as well as F1. Expected connections also vary by layout, so duplicate
client ids in `/soi-instances` are not proof of a leak.

### F5 — F-35 press-and-hold timers could survive navigation or portal removal

**Confidence: high; addressed in 0.48.1.**

TGP zoom uses a recursive timeout while held. If `renderNav()` removed the button before its
`pointerup`/`pointercancel`/`pointerleave`, the repeat could continue invisibly. Current code cancels
the pending hold before a nav rebuild and when a portal is destroyed.

This was a real leak but requires a particular mid-hold race and is unlikely to be the primary cause
of passive multi-gigabyte growth.

### F6 — the native TGP producer is bounded

**Confidence: high.**

The C# TGP pipeline does not contain an unbounded frame queue:

- only one GPU readback is allowed in flight;
- the encoder has one pending slot and replaces stale work;
- the MJPEG endpoint retains only the latest JPEG;
- each client writes frames sequentially;
- subscriber accounting is decremented in a `finally` block.

This makes a multi-gigabyte managed queue in the plugin unlikely. Browser renderer, image-decoder,
network-resource, and GPU allocations remain the more likely locations.

### F7 — continuous HIGH/HIGH TGP traffic can reproduce the ticket's scale

**Confidence: medium; correlation, not proof.**

A live local sample using 1080x720, JPEG 90 produced frames around 200 KB. The MJPEG handler polls at
40 ms, so a client can receive approximately 25 frames/s, or about 5 MB/s. That is approximately
6 GB transferred in 20 minutes and 9 GB in 30 minutes per subscriber — close to the ticket's reported
RAM and time range if some browser-native resource grows with the multipart response.

Current Chromium source updates the image and clears the current multipart buffer; it does not
provide evidence that Chromium intentionally retains the full response. An analogous ustreamer
report nevertheless observed Chrome/Edge GPU memory growing during MJPEG playback and dropping when
the stream stopped. Treat this as a browser/runtime hypothesis requiring measurement, not a proven
NOXMFD leak.

References:

- [Chromium `ImageResource` multipart implementation](https://chromium.googlesource.com/chromium/src/+/052831f0220b79fe0c3343b49f6d2863ea6de05d/third_party/blink/renderer/core/loader/resource/image_resource.cc)
- [ustreamer issue #113 — Chromium MJPEG GPU memory growth](https://github.com/pikvm/ustreamer/issues/113)

## Preferred implementation plan

### Phase 0 — establish a reproducible memory baseline

Do this before changing the image URL so the result can be compared against the same run:

1. Record the browser, version, OS, hardware acceleration state, layout, portal/pane count, mission,
   and actual captured map dimensions.
2. Start from a fresh browser process and open one display.
3. Record renderer private memory, GPU-process memory, JavaScript heap, document count, and
   `/soi-instances` connection count.
4. Perform 50 MAP -> WPT -> MAP round trips at a fixed pace.
5. Record memory after every five returns to MAP and again after waiting two minutes without input.
6. Repeat once in classic full view, classic split, and F-35. Classic full is the control because its
   canonical MAP document should remain loaded.
7. In the network log, count distinct `/map?t=` URLs and confirm the response dimensions and bytes.

Do not use total browser memory alone. Separating renderer and GPU growth distinguishes retained DOM
or JavaScript from decoded-image/texture pressure. A forced GC is useful diagnostically but must not
be part of the acceptance criterion.

### Phase 1 — give each captured map a stable identity

Preferred design:

1. Add a monotonically increasing map generation in `CapturedAssetEndpoint`.
2. Increment it when a captured map is installed and when mission map state is cleared.
3. Expose the generation alongside the telemetry map metadata.
4. Load `/map?v=<generation>` only when a nonzero/ready generation is available.
5. Use the same URL for every MAP document viewing the same captured bytes.
6. Give successful versioned responses a cache policy suitable for immutable content.
7. Give missing/not-ready responses `Cache-Control: no-store` so an early 404 cannot suppress a
   later successful capture.

This preserves cache-busting when the image genuinely changes while allowing all views of one
mission map to share one browser resource. It is preferable to `Date.now()`, which encodes request
time rather than content identity.

Lower-cost fallback:

- use a stable `/map` URL and `Cache-Control: no-cache` or `no-store`;
- remove timestamp parameters from successful initial loads;
- ensure early 404 responses are not cached.

The fallback is easier but either revalidates/retransfers the map on every new MAP document or gives
weaker guarantees when a mission changes. Implement the generation design unless live tests expose
a compatibility issue.

### Phase 2 — deterministic MAP teardown

Create one idempotent teardown function and call it from `pagehide`. It should:

1. call `source.disconnect()`;
2. clear `mapRetryTimer` and `threatTimer`;
3. cancel any pending animation frame owned by the page;
4. disconnect the stored `ResizeObserver`;
5. remove `mapImg`'s `src` and image callbacks;
6. set `overlay.width = 0` and `overlay.height = 0` to release its backing store;
7. clear or zero pre-tinted canvases if measurement shows they materially contribute.

Do not duplicate this list across event handlers. The teardown function must tolerate repeated calls
and must prevent retries or callbacks from re-acquiring resources after teardown begins.

### Phase 3 — use replacement navigation for MFD page changes

Evaluate replacing shell-controlled iframe navigation with `contentWindow.location.replace(url)` or
an equivalent helper in F-35 portals and classic split panes. The user is choosing a page on a
display, not asking the browser to preserve a back-stack entry for every bezel press.

This is secondary to F1 because iframe documents are not independently BFCache-restored, but it
keeps nested history bounded and makes the shell's ownership intent explicit. Verify initial
`about:blank`, removed iframe, and cross-document timing behavior before applying it generically.

### Phase 4 — profile one continuously visible TGP stream

After MAP navigation memory plateaus, run one TGP subscriber for 30-60 minutes at:

- native/MID JPEG/15 Hz baseline;
- HIGH resolution/HIGH JPEG/15 Hz;
- HIGH resolution/HIGH JPEG/60 Hz.

Record renderer/GPU memory, bytes per frame, delivered frames per second, encoder time, encoder drops,
and subscriber count. Repeat with hardware acceleration on and off in the affected Windows browser.

If a single visible stream still grows linearly while JavaScript heap and document count stay flat,
the next mitigation should bound the lifetime of the browser-native multipart resource:

1. Prototype periodic MJPEG reconnection with no overlapping streams and no orphaned retry timers.
2. If memory does not plateau or reconnect flashes are unacceptable, add a finite latest-frame HTTP
   endpoint and consume it with one in-flight request, `createImageBitmap()`, canvas drawing, and
   explicit `bitmap.close()` backpressure.
3. Retain MJPEG as a compatibility fallback until browser coverage is established.

The alternative transport work already surveyed in `docs/tgp-quality-alternatives.md` should only be
undertaken if these measurements show that lifecycle and finite-resource fixes are insufficient.

### Phase 5 — align TGP production and delivery work

The TGP capture setting permits 60 Hz, while the current MJPEG client loop polls every 40 ms and can
deliver at most about 25 distinct frames/s. Even with bounded queues, producing frames that cannot be
delivered wastes camera, readback, JPEG, allocation, and network-preparation work.

After fixing correctness, either:

- make delivery cadence follow the configured rate safely;
- clamp effective capture work to the deliverable rate; or
- clearly warn that rates above the transport ceiling add producer cost without equivalent browser
  frame rate.

Do not silently change an existing player setting until measurements establish the intended policy.

## Automated test plan

### MAP resource identity

- Two MAP initializations with the same generation produce the same `/map?v=` URL.
- A new generation produces a different URL.
- A not-ready generation does not repeatedly create unique image URLs.
- Retry behavior cannot cache a 404 and cannot create unbounded resource keys.
- Mission clear followed by a new capture selects the new generation.

### MAP teardown

- `pagehide` closes the `EventSource` and watchdog once.
- Teardown clears retry and threat timers.
- Teardown disconnects the observer.
- Teardown removes the image source and zeroes the canvas.
- A late image error/load callback cannot schedule work after teardown.
- Calling teardown twice is safe.

### Shell navigation

- F-35 MAP -> WPT -> MAP uses the intended replacement navigation path.
- Classic split MAP -> WPT -> MAP does the same.
- Classic full MAP -> WPT does not destroy the canonical MAP telemetry tap.
- Hidden TGP pages still unload and subscriber count returns to the visible-page count.
- Split entry cannot leave a hidden full-view TGP subscriber.

### TGP lifecycle

- `pagehide` clears retry state and removes the MJPEG source.
- A late `error` event cannot reopen the stream after teardown.
- Any periodic reconnect prototype maintains exactly one active request.
- Reconnect and error retry timers cannot overlap.

## Live verification matrix

Browsers and platforms:

- Chrome and Edge on the reported Windows 10 LTSC environment;
- current Windows 11 Chrome/Edge;
- Firefox on Windows as a different engine;
- Android Chrome as the reported non-reproduction control.

Layouts:

- classic full view;
- classic split, both pane orientations;
- F-35 with one MAP portal;
- F-35 with two to four portals, including multiple visible MAP or TGP pages.

Scenarios:

- open no MAP or TGP page;
- 50-100 MAP <-> WPT transitions;
- MAP -> MAIN and MAP -> another frame page;
- full TGP -> MAIN/MAP;
- full TGP -> split;
- repeated split TGP navigation;
- one visible TGP for 30-60 minutes;
- multiple visible TGP subscribers;
- mission exit and a second mission/map without refreshing the browser.

## Acceptance criteria

Issue #85 is ready to close only when all of the following hold:

- MAP -> WPT -> MAP reaches a stable memory plateau rather than adding one large decoded-map
  allocation per visit.
- All MAP documents viewing the same captured image use one stable resource identity.
- A new mission map replaces the old image without requiring a browser refresh.
- Renderer and GPU memory settle after the navigation stress test without a forced GC.
- `/stream` connection count matches the intentional telemetry taps and visible MAP pages.
- TGP subscriber count matches the number of visible TGP pages and falls within one to two seconds
  after leaving TGP.
- No hidden page continues telemetry parsing, image retry, animation, or hold/repeat commands.
- One continuously visible TGP stream either plateaus for 30-60 minutes or has a separately measured,
  documented transport mitigation before the issue is considered resolved.
- The behavior is verified on the affected Windows configuration and the Android control.

## Documentation follow-up

After implementation and live validation:

- update this document's status with measured before/after results and the selected design;
- update `docs/performance.md` with browser-memory guidance and any TGP rate limitation;
- update `docs/tgp-quality-alternatives.md` if finite-frame transport or MJPEG recycling is selected;
- add release notes summarizing the user-visible fix;
- backfill issue #85 with the confirmed root cause, affected layouts/browsers, validation evidence,
  and the released version.

