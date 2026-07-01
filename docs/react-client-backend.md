# React client — wiring up a real backend (planning)

## Status

Planning only. No code yet. This is the **follow-on** to
`docs/react-client.md`: it describes the work needed to point the
mock-driven React client (`react-client/`) at real data and have the
plugin serve the built app. The frontend plan deliberately isolates the
mock layer so this swap stays small — this doc enumerates everything
that swap actually touches.

Prerequisite: the React client exists and reaches feature parity on
mocked data first (see `docs/react-client.md`).

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
| `/config`               | JSON      | Ready — `{ localhost, lanUrl, port }` |

Floating-origin is already resolved server-side, so no client-side world
maths is added. The data contract the mock layer was built against is the
real one.

Two pieces this doc used to call "work to do" already exist: `TelemetryServer`
now serves the in-mod frontend as **real static files** (`ServeAsset`/
`ServeAssetRel` — MIME mapping, ETag caching, embedded-resource manifest), and
the **`/config`** endpoint already returns `{ localhost, lanUrl, port }`. So the
remaining backend work is mostly *reusing* that machinery for a React build,
not building it (see work items 3–4).

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
  `http://localhost:5005` (or a custom `Network.Port`) while prod is
  same-origin. The client can also read the live port from `/config`.

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

## Work item 3 — Serve the React build from the plugin (now mostly reuse)

`TelemetryServer` **already** serves the in-mod frontend as static files —
`ServeAsset`/`ServeAssetRel` resolve a request path to an embedded-resource
stream with MIME mapping and ETag caching. Serving a built React app extends
that path rather than adding it from scratch:

- Serve `index.html` + hashed JS/CSS/asset files out of the Vite `dist/`
  through the same resolver.
- MIME-type mapping already exists; add any types the Vite build emits
  (`.svg`, `.woff2`, source maps) and cache headers (immutable for hashed
  assets, no-cache for `index.html`).
- **SPA fallback:** any unknown path — and `/mfd` — returns `index.html`
  so the React router handles routing client-side. The existing
  data-endpoint routes must be matched *before* the fallback.

Then a **deploy step** to get `dist/` into the DLL/plugin:

- **Option A (simpler first):** post-build copy `dist/` into
  `BepInEx/plugins/noxmfd-web/`; the server reads from disk.
- **Option B (single-file, neat):** embed `dist/` as embedded resources —
  **exactly how the in-mod `src/web/` frontend already ships** (`<EmbeddedResource>`
  + the manifest resolver), so the code path is proven. Bigger DLL; preferred
  if single-file shipping matters.

## Work item 4 — The LAN-URL detail — DONE

This is already solved. `GET /config` returns `{ localhost, lanUrl, port }`
at runtime; the in-mod shell + MAIN card fetch it on load (the old
`{{LAN_URL_BLOCK}}` string-substitution path is gone). A React client does
the same:

- Fetch `/config` on load; render the LAN URL and use `port` as needed.
- Include `/config` in the dev proxy list (work item 2).
- Only net-new work here is any *additional* fields React wants (version,
  feature flags) — add them to the existing `/config` payload.

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

- The old HTML-in-C# pages (`ClientPage.cs` / `MfdPage.cs`) are **already
  gone** — the in-mod frontend is real `src/web/` files. So the choice here
  is whether the React app *replaces* that `src/web/` frontend or ships
  **alongside** it (e.g. React at `/mfd`, the current frontend at `/`) as a
  selectable alternative. Keeping both is cheap and hedges the rewrite.
- The tunable **port** part of the README's "config for tunable port/rates"
  next step is **done** (`Network.Port`); tunable *rates* remain open.

## Rough effort ranking

1. **Client transport swap** (work item 1) — small, by design.
2. **Serve the React build** (work item 3) — now mostly reuse of the
   existing static-file serving; the deploy step is the real work.
3. **Dev proxy** (work item 2) — small; main risk is SSE/MJPEG passthrough.
4. **Contract sync** (work item 5) — discipline, not one-time effort.
5. ~~**`/config` endpoint** (work item 4)~~ — **done** (already served).

## Out of scope

- A separate standalone backend process (relay/proxy service, auth,
  recording, multi-client). Only worth it if the UI must be reachable
  without the in-process game server — not a near-term need.
- Changing the telemetry data itself or adding new endpoints beyond
  `/config`.
- Anything in `docs/react-client.md` (the frontend build) — that lands
  first; this doc assumes it's done.

## Pre-flight before implementing

- Confirm the React client is feature-complete on mocks first.
- Prototype the Vite dev proxy against a running game session and verify
  `/stream` (SSE) and `/tgp.mjpg` (MJPEG) stream through it.
- Decide deploy Option A vs B (disk copy vs embedded resources) before
  writing the static-file handler.
