# React client — wiring up a real backend (planning)

## Status

Planning only. No code yet. This is the **follow-on** to
`todo/react-client.md`: it describes the work needed to point the
mock-driven React client (`react-client/`) at real data and have the
plugin serve the built app. The frontend plan deliberately isolates the
mock layer so this swap stays small — this doc enumerates everything
that swap actually touches.

Prerequisite: the React client exists and reaches feature parity on
mocked data first (see `todo/react-client.md`).

## The key framing: the backend mostly already exists

`TelemetryServer.cs` already serves browser-native, React-ready
endpoints. We are not building a telemetry backend — we're *serving the
React build from it* and *swapping the client's mock transport for the
real one*. What already works, unchanged:

| Endpoint                | Transport | Status                         |
|-------------------------|-----------|--------------------------------|
| `/stream`               | SSE 10 Hz | Ready — browser-native         |
| `/tgp.mjpg`             | MJPEG     | Ready — renders in `<img>`     |
| `/map`                  | PNG       | Ready                          |
| `/icon` `/weapon` `/cm` | PNG       | Ready (keyed by query param)   |
| `/airframe` `/airframe-layout` | PNG / JSON | Ready                  |

Floating-origin is already resolved server-side, so no client-side world
maths is added. The data contract the mock layer was built against is the
real one.

## Work item 1 — Client: swap the mock module for a real transport

By design the mock lives behind one boundary (`mock/mockEngine`,
`mock/mockAssets`, exposed via the `useTelemetry()` hook). The swap:

- `mockEngine` → `new EventSource('/stream')`; map the parsed frame onto
  the same typed shape the mock emitted.
- `mockAssets` URL resolution → real endpoint URLs
  (`/icon?type=`, `/weapon?name=`, `/cm?type=`, `/airframe?type=&part=`).
- TGP page placeholder → `<img src="/tgp.mjpg">`, gated by the same
  `tgpActive` flag and NO TARGET fallback the mock already drives.
- Add a base-URL config (`VITE_API_BASE`) so dev can point at
  `http://localhost:5005` while prod is same-origin.

Because the rest of the app only ever talked to `useTelemetry()` and the
asset resolver, nothing else should change. Keep the mock modules around
behind a build flag for offline development and parity diffing.

## Work item 2 — Dev-time cross-origin wiring

The Vite dev server (`:5173`) and the plugin server (`:5005`) are
different origins. Prefer the option that doesn't touch the C# server:

- **Vite dev proxy** (`server.proxy` in `vite.config.ts`) forwarding
  `/stream`, `/tgp.mjpg`, `/map`, `/icon`, `/weapon`, `/cm`,
  `/airframe*`, `/config` to `http://localhost:5005`. **Verify SSE and
  MJPEG pass through the proxy** (chunked/streaming) — this is the one
  thing to test, not assume.
- Fallback only if the proxy can't stream: add CORS headers
  (`Access-Control-Allow-Origin`) to `TelemetryServer`. More invasive;
  avoid unless needed.

In production the app is served same-origin (work item 3), so CORS is a
non-issue there.

## Work item 3 — Serve the React build from the plugin (the bulk)

Today `TelemetryServer` serves `ClientPage.Html` / `MfdPage.Html` as
string literals (`TelemetryServer.cs` AcceptLoop, ~L238-246). To serve a
built React app, add **static-file serving** to the `HttpListener`:

- Serve `index.html` + hashed JS/CSS/asset files out of the Vite `dist/`.
- MIME-type mapping (`.js`, `.css`, `.png`, `.svg`, `.json`, …) and
  sensible cache headers (immutable for hashed assets, no-cache for
  `index.html`).
- **SPA fallback:** any unknown path — and `/mfd` — returns `index.html`
  so the React router handles routing client-side. The existing
  data-endpoint routes must be matched *before* the fallback.

Then a **deploy step** to put `dist/` where the server can read it:

- **Option A (simpler first):** post-build copy `dist/` into
  `BepInEx/plugins/noxmfd-web/`; the server reads from disk.
- **Option B (single-file, neat):** embed `dist/` as embedded resources
  in the DLL and serve from a resource stream. Cleaner deploy, bigger
  DLL, extra resource-reading code path. Defer unless single-file
  shipping matters.

## Work item 4 — The LAN-URL detail (small but easy to miss)

The MFD MAIN card currently gets the LAN URL by **server-side string
substitution** of `{{LAN_URL_BLOCK}}` (`TelemetryServer.cs` ~L240-243).
A static React bundle can't be string-substituted at serve time, so:

- Add a tiny **`/config` JSON endpoint** returning `LanUrl` (and any
  other server-known dynamics — port, version, feature flags later).
- The React app fetches `/config` on load and renders the LAN URL from
  it. Include `/config` in the dev proxy list (work item 2).

## Work item 5 — Data-contract sync (ongoing discipline)

Serialization is hand-rolled string concatenation in
`TelemetryServer.Serialize` (`TelemetryServer.cs` ~L487). Once React is
the real consumer, the C# frame shape and `react-client/src/types/
telemetry.ts` must stay in lockstep. Options, pick later:

- Document the frame as a **versioned contract** (add a `v` field) and
  update both sides together; or
- Generate one side from the other (e.g. a small schema → TS types step).

Lowest-effort acceptable baseline: a single documented schema both sides
cite, plus a frame-version field so a mismatch is detectable at runtime.

## Work item 6 — Cleanup once parity is reached (optional)

- Retire `ClientPage.cs` / `MfdPage.cs` once the React app fully
  replaces them — removes the large HTML-in-C# string literals and
  shrinks the plugin. Or keep them as a no-JS fallback.
- Pairs naturally with the README's already-listed "BepInEx config for
  tunable port/rates" next step.

## Rough effort ranking

1. **Static-file serving + deploy step** (work item 3) — the bulk.
2. **Client transport swap** (work item 1) — small, by design.
3. **Dev proxy** (work item 2) — small; main risk is SSE/MJPEG passthrough.
4. **`/config` endpoint** (work item 4) — tiny.
5. **Contract sync** (work item 5) — discipline, not one-time effort.

## Out of scope

- A separate standalone backend process (relay/proxy service, auth,
  recording, multi-client). Only worth it if the UI must be reachable
  without the in-process game server — not a near-term need.
- Changing the telemetry data itself or adding new endpoints beyond
  `/config`.
- Anything in `todo/react-client.md` (the frontend build) — that lands
  first; this doc assumes it's done.

## Pre-flight before implementing

- Confirm the React client is feature-complete on mocks first.
- Prototype the Vite dev proxy against a running game session and verify
  `/stream` (SSE) and `/tgp.mjpg` (MJPEG) stream through it.
- Decide deploy Option A vs B (disk copy vs embedded resources) before
  writing the static-file handler.
