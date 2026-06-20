# React Native mobile app (Android tablet) — planning

## Status

Planning only. No code yet. Builds on `todo/react-client.md` (the
mock-driven React frontend) and `todo/react-client-backend.md` (the real
data transport served by the plugin). This doc is intentionally light —
it sketches a mobile app, it doesn't fully spec it.

## Goal

A native Android app, installable on a **Samsung Galaxy Tab S9 FE**
(landscape, cockpit-side-display use), that shows the same HUD + MFD as
the React web client — consuming the plugin's telemetry server over the
**LAN** (the `LanUrl` the server already advertises, e.g.
`http://192.168.1.42:5005`). The tablet sits next to the player as a
glass MFD while the game runs on the PC.

Depends on both prior subprojects: the data contract, frame types, and
visual design are settled there first; this app reuses them on a mobile
runtime.

## The one big decision: WebView shell vs. true native port

Settle this before anything else — it changes the effort by an order of
magnitude.

- **Path A — WebView shell (recommended MVP).** A thin React Native (or
  even plain Android) app whose main screen is a full-screen `WebView`
  pointed at the plugin's served React app (`http://<host>:5005/mfd`).
  Reuses **100%** of `react-client` and its backend with zero rendering
  re-port. The native shell only adds: a server-address entry screen,
  landscape lock, keep-awake, immersive fullscreen, and reconnect
  handling. Fastest route to "installed on the tablet and working."
- **Path B — true native React Native port.** Re-implement the HUD/MFD
  with native components. Better feel, offline-capable shell, no browser
  chrome quirks — but the map overlay (HTML5 canvas today) has to be
  re-rendered with a native drawing layer, which is the bulk of the work.

**Recommendation:** ship Path A first (it's a wrapper around work already
done), then decide whether Path B is worth it based on how the WebView
actually performs on the tablet.

## What's reusable vs. net-new

| Concern              | Path A (WebView)        | Path B (native port)            |
|----------------------|-------------------------|---------------------------------|
| Frame types/contract | reused as-is (in WebView) | reused (`types/telemetry.ts`)  |
| SSE `/stream`        | reused (browser EventSource) | new — `react-native-sse`    |
| Map overlay (canvas) | reused                  | **re-port to Skia** (the bulk)  |
| MFD pages/layout     | reused                  | re-build with RN components     |
| TGP MJPEG feed       | reused (`<img>`)        | new — WebView/MJPEG component   |
| Assets               | served by plugin        | bundle or fetch from server     |

## Stack (Path A first)

- **Expo + React Native + TypeScript**, built to an APK via **EAS Build**
  (or `expo run:android` for local builds). Expo keeps the toolchain
  simple for a single-target tablet app.
- `react-native-webview` for the main display.
- `@react-native-async-storage/async-storage` to persist the server
  address.
- `expo-keep-awake` (screen stays on), `expo-screen-orientation`
  (landscape lock), immersive/fullscreen mode.

For Path B later, add: `@shopify/react-native-skia` (canvas overlay
re-port), `react-native-sse` (SSE), a WebView/MJPEG view for TGP.

## Native shell responsibilities (both paths)

- **Server address entry.** Manual `host:port` input, persisted; default
  to the last-used value. (mDNS auto-discovery is a nice-to-have, later.)
- **Connection state UX** — reachable / unreachable / reconnecting, with
  a retry affordance, since Wi-Fi on a tablet drops.
- **Landscape lock + keep-awake + immersive fullscreen** — it's a fixed
  cockpit display, not a phone app.

## Distribution onto the tablet

- Build an APK with EAS (`eas build -p android --profile preview`) or
  locally, then **sideload** it: enable Developer Mode + USB debugging on
  the Tab S9 FE and `adb install`, or transfer the APK and install.
- No Play Store; this is a personal/sideloaded tool.

## Open questions (decide while building, not now)

- Path A vs B for the long term (A first regardless).
- Server discovery: manual entry only, or add mDNS/zeroconf later.
- Whether the tablet should cache the last frame / show a graceful
  "signal lost" state when the PC server goes away.

## Out of scope

- iOS / other devices — Android tablet only for now.
- Any change to the plugin server beyond what
  `todo/react-client-backend.md` already covers (it serves the app +
  advertises the LAN URL; that's enough for the WebView path).
- A native Path B port — not until Path A proves the concept on-device.

## Pre-flight before implementing

- `react-client` + `react-client-backend` must be working: the plugin
  has to serve the React app and advertise its LAN URL.
- On the same Wi-Fi, open `http://<host>:5005/mfd` in the tablet's
  browser first — if that works, Path A is just wrapping it.
