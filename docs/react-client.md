# Standalone React client (`react-client/`) — planning

## Status

Planning only. No code yet. This describes a **new sibling subproject**
that re-implements the project's browser frontend in React, driven
entirely by **mocked data** — a **future alternative** to the in-mod
frontend, not a replacement for it. The shipping frontend now lives as
real `.html`/`.css`/`.js` files under `src/web/` (served by
`TelemetryServer`) and stays the shipping implementation; the React app is
a parallel, backend-free rebuild that uses those pages only as a reference.

## Goal

Stand up `react-client/` as an independent Vite + React + TypeScript app
that visually and behaviourally replicates **every** current frontend
feature, but feeds itself from a mock layer instead of the live game.
No HTTP/SSE/MJPEG calls to a real server; no dependency on Nuclear
Option running. The deliverable is a thing you can `npm run dev` and see
the full HUD + MFD, animated by fake telemetry.

Non-goal (this phase): wiring the React app to the real `/stream`,
`/map`, `/tgp.mjpg`, etc. That's a later phase — the mock layer is
designed so swapping in a real transport is a single-module change.

## Why a separate subproject (not a rewrite of the in-mod frontend)

- The in-mod frontend under `src/web/` works and ships today (real
  `.html`/`.css`/`.js`, one source of truth per page). This subproject is
  a **future alternative**, not a fix for it — so it stays isolated and
  doesn't destabilise what ships.
- React gives a component model, real shared state, and an HMR dev loop
  that the vanilla-JS-over-`postMessage` shell can't — and it collapses
  the iframe/`postMessage` plumbing into a component tree (see the
  architecture section).
- It's the natural base for the **mobile/native path**
  (`docs/react-native-mobile.md`) and any standalone consumer that runs
  outside the game process.
- Keeping it in the same repo (sibling folder) means the mod stays the
  single source of truth for the **data contract** and visual design, and
  the two can diverge intentionally rather than by accident.

## The data contract to mock

`tools/preview-mock.js` is effectively a finished spec for the mock
layer — it already stands in for the game. Mirror it. The real server
(`TelemetryServer.cs`) exposes:

| Endpoint            | Transport | Carries                                              |
|---------------------|-----------|------------------------------------------------------|
| `/stream`           | SSE (10 Hz) | One telemetry JSON frame (see below)               |
| `/map`              | PNG       | Extracted in-game map image                          |
| `/icon?type=`       | PNG       | Per-aircraft-type map icon                           |
| `/weapon?name=`     | PNG       | Per-weapon icon                                      |
| `/cm?type=`         | PNG       | Countermeasure icon (`flares`, `jammer`)             |
| `/airframe?type=&part=` | PNG   | One airframe silhouette part (or `__bg`)             |
| `/airframe-layout?type=` | JSON | Part placement descriptor for the AVN silhouette     |
| `/tgp.mjpg`         | MJPEG     | Targeting-pod camera feed (multipart/x-mixed-replace)|

The SSE frame shape (from `TelemetryServer.Serialize` +
`preview-mock.js` `DEFAULT_FRAME`):

```jsonc
{
  "ping": false, "t": 123.4,
  "name": "FS-12 Revoker", "mission": "...", "mapName": "...",
  "world": { "x": -3000, "y": 2500, "z": 2000 },
  "hdg": 45, "tas": 248, "agl": 2500, "gear": "up",
  "units": 12, "aircraft": 6,
  "map": { "valid": true, "w": 100000, "h": 100000, "ox": 50000, "oy": 50000 },
  "iconOrient": true, "iconScale": 1.1,
  "flares": 60, "flaresMax": 64, "ewKJ": 820, "ewKJMax": 1000,
  "selWeapon": "AIM-9X", "cmCat": 1, "tgpActive": true,
  "loadout":  [ { "n": "AIM-9X", "a": 2, "f": 2 } ],          // n=name a=ammo f=fullAmmo
  "colors":   { "f": "#39ff14", "e": "#ff4040", "n": "#9aa0a6" },
  "contacts": [ { "t":"Su57","x":16000,"z":-9000,"h":220,"f":2,"o":true,"s":1,"tg":1 } ],
  "parts":    [ { "n": "wing1_L", "hp": 80, "d": 0 } ]        // d=detached
}
```

Notes that matter for faithful mocking:
- `contacts[].tg` (targeted) is the source of truth for the TGL list —
  the C# TGL page derives targets from `tg`-flagged contacts and colours
  by `f` (faction: 0 neutral, 1 friendly, 2 enemy). `preview-mock.js`
  also carries a richer `targets[]` array (name/grid/range/faction) for
  preview; replicate that as the mock's TGL source.
- Floating-origin is already resolved server-side: `world.x/z` are true
  world coords and map directly to a map fraction given `map.w/h`. The
  client just needs `worldToMapFraction`. No origin maths in React.
- `ping:true` frames mean "connected, no mission" — the React mock
  should be able to emit these to exercise the idle/empty states.

## Architecture: collapse the iframe into shared state

The single biggest structural change from the current design. Today:

- the shell (`src/web/shell/mfd.js`) hosts the map page
  (`src/web/pages/map/map.js`) at `/map-view?bare` in an **iframe** and
  drives it via `postMessage`. The map iframe is the single `/stream`
  consumer and broadcasts status / loadout / cm / tgp / targets / avionics
  back up via `postMessage`, which the shell re-forwards to the other pages.

In React this becomes a **component tree with shared context** — no
iframe, no postMessage:

- `<MapView>` is a plain component (the current map page's map+overlay).
- `<MfdShell>` renders the bezel, keys, and the active MFD page, and
  renders `<MapView>` directly in its screen recess for the MAP page.
- Telemetry + view state (zoom/pan/follow, selected weapon, target list,
  parts) live in a `TelemetryContext` + a `MapViewContext`. The MFD key
  handlers mutate that shared state instead of posting messages.

`?bare` (map-only, chromeless) survives as a route/prop on `<MapView>`
so the standalone map page is still reachable on its own.

## Stack & rendering decisions

- **Vite + React 18 + TypeScript (strict) + ESLint + Prettier.**
  Standard, fast HMR, no SSR needed. `tsconfig` strict mode on and lint/
  format wired from the first commit so the codebase stays clean.
- **Plain CSS (CSS Modules) matching the existing green-CRT theme** —
  port the existing hand-written styles rather than introducing Tailwind/
  a UI kit. The look is bespoke and already defined in `src/web/shared/theme.css`
  + the per-page CSS.
  **Faithful port + light cleanup:** match the theme closely, but tidy
  spacing/typography/responsiveness as we go where the original is rough
  (small intentional deviations allowed; a full redesign is out of scope).
- **Canvas for the map overlay.** The aircraft/contact icons, trail,
  target boxes, and grid are drawn on a `<canvas>` today (`drawOverlay`,
  `drawIcon`, `tintedIcon`). Keep that: a `<MapCanvas>` component owns a
  canvas ref and redraws on each telemetry frame / view change. React
  renders the chrome (panels, labels); canvas renders the moving parts.
- **MJPEG/TGP feed:** the real feed is an `<img src="/tgp.mjpg">`. The
  mock substitutes a looping `<canvas>`/animated placeholder or a short
  asset clip, gated behind the same `tgpActive` flag + NO TARGET fallback.

## Feature inventory to replicate

### Map / HUD surface (from `src/web/pages/map/`)
- In-game map image background + NO SIGNAL empty state.
- Own-aircraft icon: positioned by `world`, rotated by `hdg`, scaled by
  `iconScale`, with a fading **trail**.
- Other contacts: game icons, faction-tinted (`colors`), oriented (`o`),
  scaled (`s`); last-seen position for enemies.
- **Target box** overlay on `tg`-flagged contacts.
- Client-side **pan / zoom / follow** (interactive; survives in React
  state). Follow re-centres on own aircraft.
- **Grid coordinate** label (e.g. `Hc87`) reproduced from `map` offsets.
- **Hover unit label**.
- Sidebar panels: aircraft name, grid, speed (TAS km/h), AGL altitude,
  heading + gear, countermeasures (IR flares grid + EW capacitor),
  loadout list.
- Connection status + watchdog (CONNECTED / DISCONNECTED / no mission).
- Mission bar (mission name).

### MFD surface (from `src/web/shell/` + `src/web/pages/`)
- Bezel with corner keys + info box; responsive re-layout on resize.
- **MAIN** — alphabetised menu, LAN-URL card, mirrored connection status.
- **MAP** — hosts `<MapView>`; keys forward zoom in/out + follow.
- **WPN** — loadout + countermeasures, paginated (PREV/NEXT), inline IR
  flares depletion grid.
- **TGL** — target list derived from `tg` contacts, faction-coloured
  (enemies red), paginated PREV/NEXT, NO TARGETS empty state.
- **TGP** — targeting-pod feed (mock), NO TARGET fallback driven by
  `tgpActive`.
- **AVN** — airframe damage silhouette from `parts[]` HP + the
  `/airframe` part images + `/airframe-layout` descriptor; parts tint by
  HP and disappear when `detached`.
- **FLL** — browser fullscreen toggle.

## Mock layer design

A single `src/mock/` module that is the only thing the rest of the app
talks to for data — so a real transport later is a drop-in swap.

- `mockFrames.ts` — the `DEFAULT_FRAME` (ported from `preview-mock.js`)
  plus typed variants: idle/`ping`, no-mission, damaged-airframe,
  no-targets, lots-of-contacts.
- `mockEngine.ts` — emits frames on an interval like SSE. **Static by
  default**, matching the C# preview: one frozen frame resent on a timer
  to keep the watchdog happy; only client-side zoom/pan/follow move. This
  keeps the React view pixel-diffable against the C# pages. A light
  animation mode (advance `world.x/z` along heading, decay flares, cycle a
  target lock) is an optional later toggle, not the default.
- `mockAssets.ts` — resolves `/map`, `/icon`, `/weapon`, `/cm`,
  `/airframe*` to real captured assets **if a (gitignored) local assets
  folder has been populated**, otherwise to the synthetic SVGs ported
  from `preview-mock.js`. Synthetic is the committed default; real art is
  a local opt-in. A fresh clone runs with no asset files present.
- `useTelemetry()` hook — subscribes to the engine, exposes the latest
  typed frame + connection state to components.

Swapping to live data later = replace `mockEngine`/`mockAssets` with an
`EventSource('/stream')` + real asset URLs behind the same hook API.

## Proposed folder structure

```
react-client/
  index.html
  package.json  vite.config.ts  tsconfig.json
  src/
    main.tsx
    App.tsx                 # routes: standalone map, MFD shell
    types/telemetry.ts      # typed mirror of the SSE frame
    mock/                   # mockFrames, mockEngine, mockAssets, useTelemetry
    state/                  # TelemetryContext, MapViewContext
    map/                    # MapView, MapCanvas, overlay draw helpers, HUD panels
    mfd/                    # MfdShell, Bezel/Keys, pages: Main, Wpn, Tgl, Tgp, Avn
    styles/                 # ported green-CRT CSS modules
```

## Phased implementation plan

1. **Scaffold.** Vite + React + TS app under `react-client/`, theme CSS
   shell, empty routes for standalone map and MFD. `.gitignore` for
   `node_modules`/`dist`. (README note that it's mock-only.)
2. **Types + mock layer.** `types/telemetry.ts`, `mock/*`, `useTelemetry`
   emitting the default frame on a timer. Prove data flows with a debug
   dump.
3. **MapView + HUD.** Map image, canvas overlay (own aircraft + trail),
   sidebar panels, status/watchdog, grid label. Pan/zoom/follow.
4. **Contacts + targets on the map.** Tinted/oriented/scaled icons,
   target boxes, hover labels.
5. **MFD shell.** Bezel, keys, page switching, MAIN + MAP pages with
   shared state replacing postMessage.
6. **WPN + TGL pages.** Loadout/CM with pagination + flares grid; target
   list with pagination + faction colour.
7. **AVN page.** Silhouette from parts HP + layout descriptor + part art.
8. **TGP page.** Mock feed + NO TARGET fallback + fullscreen.
9. **Mock polish.** Animation mode, alternate scenario frames, parity
   pass against the C# pages.

## Locked decisions (from interview)

- **Mock motion: static by default.** Port `preview-mock.js`'s frozen-
  frame behaviour; only client-side zoom/pan/follow move. Animation is an
  optional later toggle, kept available for parity-diffing against C#.
- **Visual fidelity: faithful port + light cleanup.** Match the green-CRT
  theme closely; tidy spacing/typography/responsiveness as we go. No full
  redesign in this phase.
- **Assets: real captured ones, locally populated (gitignored).** The
  app renders real map/icons/airframe art when present, but the assets
  folder under `react-client/` is **gitignored** — populated by hand
  (copied from `preview/assets/` after a `tools/capture_assets.py` run)
  whenever a realistic preview is wanted. Nothing asset-related is
  committed. The synthetic SVGs ported from `preview-mock.js` are the
  default/fallback so a fresh clone with an empty assets folder still
  runs and looks correct.
- **Tooling: strict TS + ESLint + Prettier** from the first commit.

## Out of scope

- Any real backend integration (`/stream`, `/tgp.mjpg`, real assets).
- Changing or removing the C# frontend — it stays the shipping client.
- New frontend features not already in the C# pages. Replicate first;
  innovate later.
- Build/deploy pipeline for the React app beyond `npm run dev`/`build`.

## Pre-flight before implementing

- Re-read `tools/preview-mock.js` — it is the mock contract; the React
  mock layer should be a typed port of it.
- Skim `src/web/pages/map/map.js` (overlay draw maths, `worldToOverlay`,
  `gridLabel`, pan/zoom), `src/web/shell/mfd.js` (page list, key actions),
  and `src/web/pages/avn/avn.js` (AVN silhouette layout) for behavioural detail
  the doc summarises.
- Decide the open questions above (at least: animated mock, strictness).
