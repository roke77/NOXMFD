# `src/web/` — the MFD frontend

The whole in-mod UI lives here as real `.html` / `.css` / `.js` files, baked into the DLL as
embedded resources and served by `src/plugin/Http/TelemetryAssets.cs` (`ServeAssetRel`,
suffix-matched against the resource manifest). No C# string blobs, no bundler, no framework —
vanilla JS + `postMessage`.

Full design history and decisions: [`docs/src-architecture.md`](../../docs/src-architecture.md).

## Layout

```
src/web/
  shared/   font.css  theme.css  share-tech-mono.woff2   # passive cross-page assets
  services/ telemetry-source.js  send-command.js          # active shared code (the providers)
            pad-cursor.js                                 # the shared PAD crosshair (docs/page-cursor.md)
            remote-keybinds.js                            # opt-in browser keybind listener
  shell/    nav-model.js                                  # NAV registry — the layout seam, BOTH shells load it
            layout-pages.js                               # where each layout mounts each NAV destination
            layout-keydown.js                             # shared SAVE/LOAD LAYOUT keyboard wiring
            boot-reveal.js                                # shared boot loading-bar + typewriter mechanics
            layout-sticky.test.js                         # the classic⇄f35 redirect handoff — belongs to neither
            layout-coverage.test.js                       # every NAV destination reachable in BOTH layouts
            classic/       mfd.html  mfd.css  mfd.js       # the classic bezel shell (host + router)
                           split-keymap.js                 # bezel key-slot logic (split panes)
                           classic-paging.js               # pure split-pane pagination + list-page key layout
            f35/           f35.html  f35.css  f35.js       # a second shell: borderless F-35 glass, N portals
                           f35-glass.js  f35-wpn-paging.js  # portal merge/split geometry, WPN pagination
  pages/
    map/    map.html  map.css  map.js     # the live map view (imports services/telemetry-source.js)
            map-transform.js              # its pure world⇄pixel maths (pan/zoom/letterbox)
    wpt/    wpt.html  wpt.css  wpt.js     # waypoint/route editor, thin client over the plugin's
            waypoints-store.js            # RouteStore (docs/hud-waypoint-indicator.md) — fetch/poll
            wpt-route.js  wpt-route.test.js  # /wpt-options + POST /command, no local persistence
    wpn/  tgt/  tgp/  avn/  afm/  rwr/  rdr/  hud/  bdf/  mis/  obj/  akf/  mapcfg/  tgpcfg/
                                               # reactive MFD pages, one folder each (bdf.js doubles as PAL, ?pal;
                                               # akf = kill feed/session stats docs/akf-page.md; mapcfg/tgpcfg =
                                               # each page's own refresh-rate/quality settings, reached from that
                                               # page's own nav row, not CFG — see nav-model.js's NAV.mapcfg/tgpcfg)
                                               # some carry a pure sibling module — see below
    ext/                                       # EXT hub — lists extensions discovered at runtime via
                                               # /ext-manifest (shell/ext-nav.js), no fixed page content of
                                               # its own; each extension serves its own page under /ext/<id>
                                               # (docs/extensions-api.md)
    keybinds/                                  # frame-hosted like the pages above, not a standalone document
    main/                                      # the split-pane MAIN card (full-view MAIN is shell chrome)
```

Two shells render the same pages: the classic bezel (`shell/classic/mfd.js`) and the F-35 glass
(`shell/f35/f35.js`), sharing the NAV model, the page-routing tables (`shell/layout-pages.js`),
`sendCommand`, the opt-in remote keybind listener (`services/remote-keybinds.js`), and the
SAVE/LOAD LAYOUT keyboard wiring
(`shell/layout-keydown.js`) and the boot loading-bar/typewriter mechanics
(`shell/boot-reveal.js`) — see [`docs/layouts.md`](../../docs/layouts.md). `*.test.js` files sitting next to their module (e.g.
`nav-model.test.js`, `f35-glass.test.js`, `classic-paging.test.js`) are Node self-checks, run by hand
(`node src/web/<path>/whatever.test.js` from anywhere), never fetched by a browser (excluded from the
embedded-resource glob). The whole suite: `find src/web -name '*.test.js' -exec node {} \;`. Most are
CommonJS; the two under `services/` load their ES-module subject with dynamic `import()` and so need
Node >= 22.7, since no `package.json` declares the module type.
Logic worth checking gets split into a **pure sibling module** the page loads and a test drives
without a DOM — the page keeps the elements and live state and passes what the module needs in. The
same move the shell makes with `nav-model.js` / `classic-paging.js`. Named for what it does:
`map-transform.js` (world⇄pixel maths), `bdf-funds.js` (the magnitude-band money format),
`keybinds-keymap.js` (KeyboardEvent.code ⇄ Unity KeyCode names), `remote-keybinds.js` (the
per-browser KEY-page listener that maps configured keys into `/command` posts), `wpt-route.js`
(route/waypoint
display-derivation — bearing/distance math and a client-side pre-validator for pasted route JSON;
the actual route data and its mutation live server-side in `RouteStore`, not here — see
`waypoints-store.js`), and `<x>-*-policy.js` where the
logic really is a classification rule (`avn-status-policy.js`, `avn-throttle-policy.js`,
`afm-bg-policy.js`, `afm-failure-policy.js`). Everything else on a page is DOM-coupled rendering and
is left to the harness and the eye, not to Node asserts.

Convention per page: `src/web/pages/<x>/<x>.{html,css,js}`, served at `/<x>`. The HTML links
`/assets/shared/font.css` + `theme.css`, then its own `<x>.css`, and ends with `<script
src="/assets/pages/<x>/<x>.js">`. Add files freely — the csproj embeds `src/web/**/*`.

## Component roles — read this before touching the data path

The three roles are **not** symmetric. The clean rule ("shell funnels data down into dumb pages")
holds for every page but one: **MAP is special**: it is the single telemetry *source*, not a
reactive sink.

```
   mod /stream (SSE, ~10 Hz)
          │
          ▼
   ┌──────────────┐  The ONLY EventSource('/stream') consumer. Internally split (SRP) into
   │  MAP iframe  │  TelemetrySource (services/telemetry-source.js — owns the SSE connection, derives
   │ source +view │  the slices below, posts them UP) and the map view (map.js — renders the live
   │              │  map/HUD from the frames the source hands back). One iframe on purpose:
   │              │  the view needs the full frame every tick, so the parse stays in-process.
   │              │  Slices posted up: status·mapinfo·loadout·cm·tgp·targets·rwr·mw·rdr·avn·
   │              │  tgt·bdf·pal·mis·obj·follow·grid, plus the focus/input events
   │              │  soi-cid·soi·soi-act·cursor·cursor-held·cursor-select·map-act
   └──────┬───────┘
          │  postMessage  ▲ UP   ({ mfd:true, type, … })
          ▼
   ┌──────────────┐  Caches each slice and re-forwards DOWN to whoever is visible:
   │  SHELL       │  forwardX*ToFrame (full view) / forwardX*ToPanes (split) — or, on the F-35
   │ (mfd.js OR   │  shell, to whichever portal owns that page. Owns split/portal logic, page
   │  f35.js)     │  hosting, and the SOI focus ring + NAV cursor (derived from each page's own
   │              │  data-action / .nav-item elements). PAD-cursor pages instead get the
   │              │  raw cursor events forwarded down and draw their own crosshair.
   │              │  Guard: only trusts telemetry from the canonical MAP iframe/tap
   │              │  (e.source === mapFrame.contentWindow).
   └──────┬───────┘
          │  postMessage  ▼ DOWN
          ▼
   WPN · TGT · TGP · AVN · AFM   pure reactive renderers — render to their own container,
   RWR · RDR · HUD · BDF/PAL     never know full-vs-split, never touch /stream.
   MIS · OBJ · KEYBINDS
```

**Why MAP carries two hats (and it's deliberate):** MAP needs the raw stream anyway (live map,
floating-origin math, contacts) and must be same-origin to pull the real map PNG, so it already
holds the SSE connection. The mod's `HttpListener` SSE is happiest with **one** consumer, so rather
than open a second connection from the shell, MAP parses once and broadcasts derived state up. That
is why MAP is the **always-on base iframe** (under `#page-frame` + the overlay) and is **not** in
`FRAME_PAGES` — it has to stay connected even while you're looking at WPN/TGT, or data stops
flowing to them. (In split, a MAP *pane* also opens `/stream`, but the shell ignores its mirror
posts — only the base `mapFrame`'s posts drive the caches.)

A frame that fails to parse is **dropped, not fatal**: the server hand-rolls its JSON, so a
serializer bug arrives here as a parse throw, and an uncaught one would take down that tick's whole
fan-out — freezing every page while the SSE connection stays open and the watchdog stays quiet. The
drop is logged (rate-limited, with a running total) and the next good frame ~100 ms later repaints
everything, so a transient glitch self-heals while a persistent one still says so in the console.

**MAP view state (FLW + ZOOM)** persists in `sessionStorage` under `noxmfd.map.view`, shared
same-origin across the base map iframe and any split-pane map — so it survives page navigation,
split-pane reloads, and the mission-exit reset, and follow is mirrored up to the shell's FOLLOW
chip on (re)entry. First run seeds the defaults (follow **on**, a medium zoom). It's view-local —
not part of the data path; `map.js` owns it (`loadPersistedView` / `savePersistedView`). RDR's
selected range follows the same pattern under `noxmfd.rdr.view`.

## Hosting model

- **Full view (bezel):** the visible page renders in the shell's single `#page-frame` iframe
  (`FRAME_PAGES = {wpn, tgp, avn, afm, rwr, rdr, tgt, hud, bdf, pal, mis, obj, keys}` — the key is
  the NAV action, the value the route, which is why `pal` maps to `/bdf?pal` and `keys` to
  `/keybinds`). MAP is the base iframe *under* it; MAIN's full view is the shell's own info-box
  chrome (not a hosted page).
- **Split view (bezel):** two stacked pane iframes (`/<page>?bare` each). The shell forwards data
  to both.
- **F-35 (`shell/f35/`):** a third, N-way layout instead of full/split — up to 4 portals, each an
  independent `/<page>?bare` iframe; corner grips merge/split them (`f35-glass.js`). WPN's own
  pagination is `f35-wpn-paging.js`.
- **Every NAV destination resolves in both layouts**, so nothing renders dimmed. The two routing
  tables sit together in `shell/layout-pages.js` for that reason, and `layout-coverage.test.js`
  fails by name if one layout gains a page the other lacks.
- A page is the **single source of truth** across all of these — one file, with an optional
  `body.full` profile toggled by a `layout:'full'` field in its layout message.

## The contracts (shell ⇄ page, envelope `{ mfd:true, type, … }`)

- **Data down:** `'<page>'` (the sliced rows + selection), `'<page>-layout'` (geometry +
  `layout:'full'|'compact'`), `'cm'`, `'orient'`.
- **SOI (up then down):** `telemetry-source.js` posts `'soi-cid'` (this document's instance id,
  once, from the server's SSE `hello`), `'soi'` (`{focused, pane}`, on change), and `'soi-act'`
  (`{act, pane}`, on a HOTAS keypress) up to whichever shell hosts it; the shell reports its own
  surface count back down via `soi.panes` (below). Most pages carry no SOI-specific code — the
  shell derives their bezel/NAV cursor from their own `data-action` / `.nav-item` elements.
- **PAD cursor (the exception):** a page in `PAD_CURSOR_PAGES` (`map`, `tgt`, `hud`, `rdr`) draws a
  real crosshair over its own content, so the shell forwards the raw `'cursor'` / `'cursor-held'` /
  `'cursor-select'` / `'map-act'` events down to whichever eligible page is focused
  (`focusedCursorWindow()`), and the page integrates them with `services/pad-cursor.js`. Each page
  decides what the events *mean*: MAP hit-tests contacts and pans at the edge, TGT walks its rows,
  RDR steps its range — one HOTAS bind, per-page meaning (docs/page-cursor.md).
- **Write commands:** `src/web/services/send-command.js` POSTs the flat `{cmd, …}` envelope to `/command`
  — from pages (MAP tap → `target.select`; TGT → `tgt.*` + `target.deselect`; AVN → `avn.toggle`;
  HUD → `hud.*`/`declutter.set`/`preset.*` (issue #50 follow-up); KEYBINDS → the `keybind.*` family)
  and from either shell (`soi.panes`, `weapon.select`, `master-arms.set`, `combat-mode.set`,
  `layout.save`/`.rename`/`.delete` (issue #51 — LOAD itself is a client-side `GET /layout-options`
  read, no command), and `avn.toggle` again from the F-35 master strip). Every handler is listed in
  [`src/plugin/README.md`](../plugin/README.md).
- **Remote keybinds:** `src/web/services/remote-keybinds.js` is loaded by shells and standalone
  pages. It stays idle unless the per-browser KEY toggle is on, then polls `/keybinds-config`,
  translates configured keys into `/command` posts, and uses explicit held-state commands for
  cursor/fire actions so browser keyup/blur can release them.

## Verifying without the game

`dotnet build` checks the C# routes + embedded-resource manifest but never parses the JS/CSS. Run
the browser harness instead: `python tools/serve_web.py --open` (launch.json `hud-web`, port 8782)
serves the real `src/web/` files, mocks `/stream` (`tools/preview-mock.js` feeds the MAP iframe /
map-tap and both shells), and serves `/config` + captured assets. The mock also exposes
`window.__setSoiTarget(cid, pane)` / `window.__soiPress(act)` to drive SOI focus/keys by hand, since
nothing else moves them without a game. Drive it with the Browser pane tools (`javascript_tool` to
probe/poke state, `computer` for clicks and screenshots, `read_console_messages` /
`read_network_requests` for errors). Then confirm in-game on the next DLL build.
