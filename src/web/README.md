# `src/web/` — the MFD frontend

The whole in-mod UI lives here as real `.html` / `.css` / `.js` files, baked into the DLL as
embedded resources and served by `src/plugin/TelemetryServer.cs` (`ServeAssetRel`, suffix-matched
against the resource manifest). No C# string blobs, no bundler, no framework — vanilla JS +
`postMessage`.

Full design history and decisions: [`docs/src-architecture.md`](../../docs/src-architecture.md).

## Layout

```
src/web/
  shared/   font.css  theme.css  share-tech-mono.woff2   # passive cross-page assets
  services/ telemetry-source.js  send-command.js          # active shared code (the providers)
  shell/    mfd.html  mfd.css  mfd.js                     # the bezel shell (host + router)
  pages/
    map/    map.html  map.css  map.js     # the live map view (imports services/telemetry-source.js)
    wpn/  tgt/  tgp/  avn/  rwr/           # reactive MFD pages, one folder each
    main/                                  # the split-pane MAIN card (full-view MAIN is shell chrome)
```

Convention per page: `src/web/pages/<x>/<x>.{html,css,js}`, served at `/<x>`. The HTML links
`/assets/shared/font.css` + `theme.css`, then its own `<x>.css`, and ends with `<script
src="/assets/pages/<x>/<x>.js">`. Add files freely — the csproj embeds `src/web/**/*`.

## Component roles — read this before touching the data path

The three roles are **not** symmetric. The clean rule ("shell funnels data down into dumb pages")
holds for six of the seven pages, but **MAP is special**: it is the single telemetry *source*, not
a reactive sink.

```
   mod /stream (SSE, ~10 Hz)
          │
          ▼
   ┌──────────────┐  The ONLY EventSource('/stream') consumer. Internally split (SRP) into
   │  MAP iframe  │  TelemetrySource (services/telemetry-source.js — owns the SSE connection, derives
   │ source +view │  the slices below, posts them UP) and the map view (map.js — renders the live
   │              │  map/HUD from the frames the source hands back). One iframe on purpose:
   │              │  the view needs the full frame every tick, so the parse stays in-process.
   │              │  Slices posted up: status·loadout·cm·tgp·targets·rwr·mw·avn·follow
   └──────┬───────┘
          │  postMessage  ▲ UP   ({ mfd:true, type, … })
          ▼
   ┌──────────────┐  Caches each slice and re-forwards DOWN to whoever is visible:
   │  SHELL       │  forwardX*ToFrame (full view) / forwardX*ToPanes (split).
   │  (mfd.js)    │  Owns the bezel, split logic, page hosting, the softkey contract.
   │              │  Guard: only trusts telemetry from the canonical MAP iframe
   │              │  (e.source === mapFrame.contentWindow).
   └──────┬───────┘
          │  postMessage  ▼ DOWN
          ▼
   WPN · TGT · TGP · AVN · RWR   pure reactive renderers — render to their own container,
                                 never know full-vs-split, never touch /stream.
```

**Why MAP carries two hats (and it's deliberate):** MAP needs the raw stream anyway (live map,
floating-origin math, contacts) and must be same-origin to pull the real map PNG, so it already
holds the SSE connection. The mod's `HttpListener` SSE is happiest with **one** consumer, so rather
than open a second connection from the shell, MAP parses once and broadcasts derived state up. That
is why MAP is the **always-on base iframe** (under `#page-frame` + the overlay) and is **not** in
`FRAME_PAGES` — it has to stay connected even while you're looking at WPN/TGT, or data stops
flowing to them. (In split, a MAP *pane* also opens `/stream`, but the shell ignores its mirror
posts — only the base `mapFrame`'s posts drive the caches.)

**MAP view state (FLW + ZOOM)** persists in `sessionStorage` under `noxmfd.map.view`, shared
same-origin across the base map iframe and any split-pane map — so it survives page navigation,
split-pane reloads, and the mission-exit reset, and follow is mirrored up to the shell's FOLLOW
chip on (re)entry. First run seeds the defaults (follow **on**, a medium zoom). It's view-local —
not part of the data path; `map.js` owns it (`loadPersistedView` / `savePersistedView`).

## Hosting model

- **Full view:** the visible page renders in the shell's single `#page-frame` iframe
  (`FRAME_PAGES = {wpn, tgt, tgp, avn, rwr}`). MAP is the base iframe *under* it; MAIN's full view
  is the shell's own info-box chrome (not a hosted page).
- **Split view:** two stacked pane iframes (`/<page>?bare` each). The shell forwards data to both.
- A page is the **single source of truth** for both layouts — one file, with an optional `body.full`
  profile toggled by a `layout:'full'` field in its layout message.

## The contracts (shell ⇄ page, envelope `{ mfd:true, type, … }`)

- **Data down:** `'<page>'` (the sliced rows + selection), `'<page>-layout'` (geometry +
  `layout:'full'|'compact'`), `'cm'`, `'orient'`.
- **Softkeys up (declarative bezel keys):** a page posts `{ type:'softkeys', keys:[{ side, slot,
  label, action, data }] }`; the shell's `applySoftkeys(keys, paneOffset, maxRow)` maps each
  pane-local slot to a physical bezel key. No page emits any today (TGT drives its own clicks);
  the shell caches each pane's set so it survives a re-render of the other pane.
- **Write commands:** `src/web/services/send-command.js` POSTs the flat `{cmd, …}` envelope to `/command`
  (MAP tap → `target.select`; TGT page → `tgt.*` + `target.deselect`).

## Verifying without the game

`dotnet build` checks the C# routes + embedded-resource manifest but never parses the JS/CSS. Run
the browser harness instead: `python tools/serve_web.py --open` (launch.json `hud-web`, port 8782)
serves the real `src/web/` files, mocks `/stream` (`tools/preview-mock.js` feeds the MAP iframe),
and serves `/config` + captured assets. Drive it with the Preview MCP (`preview_eval` probes;
`preview_screenshot` times out). Then confirm in-game on the next DLL build.
