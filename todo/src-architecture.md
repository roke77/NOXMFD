# src/ web-frontend architecture — design & refactor plan

Status: **in progress** — steps 1–5 done, with step 6 shared-JS extraction remaining. Step 4 has
one known follow-up: split-mode TGL deselect still uses the legacy hand-binding. Resource plumbing,
shared font/theme, **WPN** as the proof page, then **TGL** (which
introduced the **declarative softkey contract** — full view; split deselect still on the legacy
binding), **TGP** (the targeting-pod feed — no softkeys/geometry, one profile for both layouts),
**AVN** (avionics silhouette + FUEL/THROTTLE bars — two profiles; full anchors name/frame to the
bezel geometry the shell forwards), and **RWR** (radar-warning scope — one responsive SVG, one
profile, two streams). **Step 5 is done:** MAP, MAIN, and the shell now live under `web/`, `/config`
replaced URL templating, and the preview harness serves the real files directly. Remaining: shared
JS extraction (step 6).

## Goal

1. Author the frontend as real **`.html` / `.css` / `.js`** files with proper
   tooling, instead of `const string Html = """ … """` C# blobs.
2. **One source of truth per MFD page** — eliminate the full-view vs split-view
   duplication so each page (MAP, WPN, TGL, TGP, AVN, RWR) is defined once and
   renders identically whether it's the whole screen or one split pane.
3. A **declarative bezel-softkey system** so each page describes its softkeys and
   the shell maps them to the physical bezel dynamically — split "just works".

## Decisions (settled)

- **Unification:** full **iframe unification**. Every page becomes one bare page;
  full view hosts it in **one** iframe, split hosts **two**. The full-view overlay
  renderers in `MfdPage.cs` are deleted. (MAP already works this way.)
- **Packaging:** **embedded resources**. Real files in the repo, marked
  `<EmbeddedResource>`, baked into the DLL at build. Single-DLL distribution is
  preserved; full editor tooling during authoring.
- **No JS framework / bundler.** Stay vanilla JS + ES modules + the existing
  `postMessage` protocol. No Node toolchain. *(This is about the mod-served
  frontend. The planned React client — `todo/react-client*.md` — is a separate
  consumer of the same telemetry/command HTTP API and is out of scope here; this
  refactor cleans up the in-mod UI, it doesn't replace it.)*

## Historical baseline (the original problem, with evidence)

> **Note (historical baseline):** this section describes the *pre-migration* state and its
> line numbers are from before any work landed. WPN is now migrated (see the Migration plan
> and the **Implementation playbook** below); treat the line numbers here as approximate
> history, and `grep` for the real current locations.

Three layers of duplication, all stemming from the old "HTML lives in C# strings" model:

### 1. Everything is a `const string Html` blob
Each page is `internal static class XxxPage { public const string Html = """ …
HTML + <style> + <script> … """; }`, served by `TelemetryServer` via `ServePage`
(`TelemetryServer.cs:279-306`). No highlighting, no linting, no formatting, no
reuse. Editing required `tools/build_preview.py` to extract the blob back out to
a browser-testable file. Pre-migration sizes:

| File | Lines | Role |
|---|---|---|
| `MfdPage.cs` | 3142 | the shell (bezel, split logic, **+ full-view overlay renderers**) |
| `TelemetryReader.cs` | 1191 | (C# — not frontend) |
| `ClientPage.cs` | 1161 | the MAP page (`/map-view`) |
| `TelemetryServer.cs` | 757 | (C# — routes + serving) |
| `AvnPage.cs` | 631 | bare AVN page (split only) |
| `WpnPage.cs` | 405 | bare WPN page (split only) |
| `TglPage.cs` | 206 | bare TGL page (split only) |
| `RwrPage.cs` | 161 | bare RWR page (split only) |
| `TgpPage.cs` | 94 | bare TGP page (split only) |
| `MainPage.cs` | 83 | the MAIN menu page |

### 2. The embedded font is duplicated 8×
The ~50 KB base64 `woff2` `@font-face` block is copy-pasted into **8** files:
`AvnPage, ClientPage, MainPage, MfdPage, RwrPage, TglPage, TgpPage, WpnPage`.
≈ 400 KB of duplicated base64. Same for the shared green-theme CSS (colours,
`Share Tech Mono` stack, the `.cm-*` / `.wp-*` classes).

### 3. Each MFD page is rendered TWICE (the full/split duplication)
- **Full view** is drawn by **overlay panels inside `MfdPage.cs`** — the markup
  (`MfdPage.cs:1160-1201`: `wpn-panel`, `tgp-panel`, `tgl-panel`, `avn-panel`)
  and the renderers `renderWpn` (2469), `renderCm` (2595), `renderTgl` (2672),
  `renderAvn` (2112).
- **Split view** is drawn by the **separate bare pages** (`WpnPage.cs`, etc.)
  loaded as **iframes** (`MfdPage.cs:1138-1140`, sources set in `applySplitMode`).

So WPN's layout, CSS, and bezel bindings exist in *both* `MfdPage.cs` and
`WpnPage.cs`. The `cm-panel` markup at `MfdPage.cs:1162` is byte-identical to
`WpnPage.cs:190`. The bezel mapping is likewise hand-coded twice — once in each
overlay renderer, once in `renderSplitLabels` with `paneOffset` arithmetic.

**MAP is the exception that proves the target.** In both modes it's the same
`/map-view?bare` iframe (`MfdPage.cs:1133` full, pane sources in split). One
source of truth; the shell just hosts one or two of them.

## Target architecture

### A. The shell hosts pages; pages don't know about modes
`MfdPage` (the shell) owns: the bezel frame, the screen recess, power/black-out,
the info-box, split divider, and **iframe hosting**. It renders:
- **full view:** one full-size iframe → `/<page>?bare`
- **split view:** two stacked iframes → `/<top>?bare` and `/<bot>?bare`

A page is a pure reactive renderer inside its iframe. It renders to **its own
container size** and already receives geometry/data via `postMessage`. It never
needs to know full-vs-split — that distinction disappears from page code
entirely. (This generalises what MAP/`ClientPage` + the bare pages already do.)

Net deletion: all overlay panel markup + `renderWpn/renderCm/renderTgl/renderAvn`
in `MfdPage.cs`. The shell shrinks substantially; each page keeps exactly one
renderer (the bare page, promoted to the single source of truth).

### B. Declarative bezel-softkey contract
Replace the twice-hand-coded bezel binding with a one-way contract:

- A page computes its softkeys for the current state and posts them up:
  ```js
  parent.postMessage({ type: 'softkeys', keys: [
    { side: 'left',  slot: 1, label: 'DESEL', action: 'target.deselect', data: { id: 1234 } },
    { side: 'right', slot: 0, label: 'NEXT',  action: 'page.next' },
  ]}, '*');
  ```
  `slot` is **pane-local**: `0` = top band, `1`/`2` = the two keys below.
- The **shell** owns the single physical mapping:
  `physicalKey = keyBanks[side][slot + paneOffset]`, where `paneOffset = 0` for
  full view / split-top and `3` for split-bottom (the existing `keyBanks` /
  `paneOffset` model in `MfdPage.cs:1262`, `renderSplitLabels:1524`).
- The shell sets the label + binds the click; clicking sends the `action`
  (+`data`) back into that pane's iframe (or to `/command` for write actions like
  `target.deselect`). One protocol, computed once per page, mapped once by the
  shell. Split falls out for free.

This subsumes today's `tgl-deselect` bezel binding (which is hand-wired in both
the overlay `renderTgl` and `renderSplitLabels`) into a single declarative path.

### C. Proposed file layout
```
web/
  shared/
    theme.css            # green theme, colours, layout primitives
    font.css             # the @font-face (the woff2 lives here, ONCE)
    sse-client.js        # SSE connect/parse (today duplicated in ClientPage + panes)
    mfd-protocol.js      # postMessage envelope helpers (data down, softkeys up)
    sendCommand.js       # flat /command POST helper (today in ClientPage + MfdPage)
  shell/
    mfd.html  mfd.css  mfd.js     # the shell (was MfdPage.cs)
  pages/
    wpn/ wpn.html wpn.css wpn.js  # DONE (was WpnPage.cs; now full + split in one file)
    tgl/ tgp/ avn/ rwr/ main/ map/   # one folder per page (map was ClientPage.cs)
```
**Convention (adopted):** one folder per page, `web/pages/<x>/<x>.{html,css,js}`. The page
links `/assets/shared/font.css` + `theme.css` then `/assets/pages/<x>/<x>.css`, and ends with
`<script src="/assets/pages/<x>/<x>.js">`. Served at `/<x>` via `ServeAssetRel`.
`.csproj`: one `<EmbeddedResource Include="web/**/*" />`. `TelemetryServer`
resolves a request path → resource stream (a small static-asset map / convention)
and serves with the right content-type, replacing the per-page `XxxPage.Html`
constants and the `{{LAN_URL_BLOCK}}` string replace (becomes a runtime inject or
a tiny templating pass).

### D. Build / preview impact
The real `web/` files are directly servable. `tools/serve_web.py` serves the shell,
pages, `/config`, captured assets, and the MAP mock layer. `tools/build_preview.py`
is now only a compatibility cleanup helper that removes stale generated preview
HTML; no C# string extraction remains.

## Migration plan (incremental, each step shippable)

The DLL keeps building and the UI keeps working after every step.

1. ~~**Resource plumbing.**~~ **DONE.** `web/` + `<EmbeddedResource>`; `TelemetryServer`
   serves `web/` files via `ServeAsset`/`ServeAssetRel` under `/assets/` (suffix-matched).
2. ~~**Extract shared font + theme.**~~ **DONE.** `web/shared/font.css` (woff2 externalised
   to one binary), `theme.css` (base + colour tokens). WPN references them; other pages
   de-dup as they migrate.
3. ~~**Convert one page end-to-end as the proof (WPN).**~~ **DONE.** WPN now lives in
   `web/pages/wpn/{wpn.html,wpn.css,wpn.js}` with two layout profiles in one file
   (compact = split pane, full = full screen). The shell hosts full-view WPN in a
   `#page-frame` iframe (forwarding full-screen geometry from the bezel separators) and the
   old `wpn-panel` overlay + `renderWpn`/`renderCm` + their markup/CSS are deleted. Note:
   WPN needs **no softkey contract** — its only keys are nav (MAIN/PREV/NEXT), which stay
   shell-owned because pagination is shell state. The softkey contract arrives with TGL
   (step 4), which has per-target (deselect) keys. Verified in a shell harness over http.
4. **Roll the pattern to TGL, TGP, AVN, RWR, MAIN.** Each: file split (into `web/pages/<x>/`),
   drop the overlay, host full-view in `#page-frame`. **TGL: DONE** —
   `web/pages/tgl/{tgl.html,tgl.css,tgl.js}`, overlay + `renderTgl` deleted, and the
   **declarative softkey contract** landed (page emits `{side,slot,label,
   action:'target.deselect',data:{id}}`; shell `applyFrameSoftkeys` maps `slot+paneOffset`).
   **Caveat:** full view uses the contract; **split-mode deselect still rides the legacy
   `renderSplitLabels` hand-binding** (`mfdButton`'s `tgl-deselect` case) — folding split onto
   the contract is the remaining contract work.
   **TGP: DONE** — `web/pages/tgp/{tgp.html,tgp.css,tgp.js}`, overlay (`.tgp-panel` markup/CSS +
   `tgpPanel`/`tgpImg` refs + MJPEG handling) deleted, hosted in `#page-frame` via
   `forwardTgpToFrame`. The simplest page: no key-band geometry, no pagination, no softkeys, so
   **no separate `full` profile** — the centred 3:2 feed renders identically in both layouts
   (like MAP). `PAGES.tgp` flipped to `opaque:false`; its only key is the static MAIN label.
   **AVN: DONE** (verified in-game) — `web/pages/avn/{avn.html,avn.css,avn.js}`, the largest
   page (shell shed ~660 lines: ~300 CSS + ~360 JS + markup/refs/state). **Two profiles:**
   compact (split pane) keeps the fixed name/frame pixel offsets; full (`body.full`) overrides
   them — name vertical-centred on the bezel `key[0]` row + sized up, frame spanning
   `sep[1]`..last-sep. The shell forwards that geometry via a new `avn-layout` message
   (`forwardAvnLayoutToFrame`, translated into frame coords); the page applies it or falls back
   to the compact CSS. `forwardAvnToFrame` mirrors `forwardAvnToPanes`. Kept `avnData` (forwarders
   read it); deleted `AVN_FAILURE_DEFS` + the `/airframe` layout cache/retry state + all
   `renderAvn..positionAvnBarValue`. **Harness note:** `serve_web.py` now serves captured
   `/airframe[-layout]` data from `preview/assets/manifest.json`, so silhouettes render in the
   HTTP harness when a capture exists (otherwise the page uses its normal no-silhouette fallback).
   **RWR: DONE** — `web/pages/rwr/{rwr.html,rwr.css,rwr.js}`. Like TGP, one responsive SVG
   (1000×1000 viewBox, `preserveAspectRatio` meet) → **no separate `full` profile**. Two data
   streams: `forwardRwrToFrame` (contacts) + `forwardMwToFrame` (incoming missiles); kept
   `rwrData` + `mwData` (the forwarders read them); deleted `RWR_COL`/`rwrShort`/`renderRwr`/
   `renderThreats` + the missile-flicker timer. `opaque:false`; only key is the static MAIN label.
   **MAIN: DONE in step 5b/5c** — its split-pane card lives in `web/pages/main/`; its full view
   is the shell's info-box + boot loader in `web/shell/` (startup chrome, not page content).
   LAN URLs now come from runtime `/config`, so there is no server-side HTML templating path left.
5. ~~**Convert MAP, MAIN, and the shell**~~ **DONE.** `web/pages/map/`, `web/pages/main/`, and
   `web/shell/` are the live frontend sources; `ClientPage.cs`, `MainPage.cs`, and `MfdPage.cs`
   are deleted.
6. **Extract shared JS** (`sse-client`, `mfd-protocol`, `sendCommand`) and
   de-duplicate the now-parallel copies across pages.
7. ~~**Simplify preview tooling**~~ **DONE (pulled into 5c).** `serve_web.py` serves `web/`
   directly; `build_preview.py` only removes stale generated files.

## Step 5 execution plan (MAP + MAIN + shell)

Scoped 2026-06-27. Step 5 is the highest-blast-radius work: MAP is the **single SSE tap** and
the shell **is** everything, so a regression breaks the whole MFD (not one page). Hence: split
into shippable sub-steps, lowest-risk first, and keep the preview harness green before each
in-game check.

**The data flow to preserve (do not break):** `/stream` → the **map iframe** (`ClientPage`, the
only `EventSource('/stream')`) → it parses + `parent.postMessage`-es derived state up
(`status`/`loadout`/`cm`/`tgp`/`targets`/`rwr`/`mw`/`avn`/`follow`) → the **shell** caches it and
re-forwards to the `#page-frame` (full) or panes (split). MAP is the always-on **base layer**
(`iframe[title=map]` under `#page-frame` + `.overlay`), **not** a `#page-frame` page — moving its
file must keep it as that base data-tap. The shell's relay guard `e.source === mapFrame.contentWindow`
must survive the move intact.

**Locked decisions:**
- **D1 — LAN URLs → `/config` endpoint: DONE.** `GET /config` returns JSON `{ localhost, lanUrl, port }`.
  The shell + the MAIN card `fetch('/config')` on load and fill `.ib-url`. `TelemetryServer` dropped
  the `{{LAN_URL_BLOCK}}`/localhost string-replace (resolves open question #4). No HTML templating.
- **D2 — the info-box + boot loader stay SHELL chrome** (in `web/shell/mfd.*`). They're power-on
  furniture (`flickerScreen`/`runBootLoading`/`typewriterUrls`), not page content. `web/pages/main/`
  is only the **split-pane card** (shares `theme.css`). Accept the minor card duplication; the boot
  animation never has to run inside an iframe. (So MAIN is NOT hosted in `#page-frame` — full-view
  MAIN remains the shell's `#info-box`.)
- **D3 — faithful move first, dedup later.** Do step 5 as a behaviour-preserving move; the shared-JS
  extraction (`sendCommand`/SSE/postMessage) is step 6, after.

**Sub-steps (each builds + verifies in the harness, then in-game):**
- **5a — MAP: DONE.** `web/pages/map/{map.html,map.css,map.js}`; point `/map-view` at `ServeAssetRel`;
  delete `ClientPage.cs`. It stays the base data-tap iframe. Biggest single file (~1161 lines) but
  mechanically a move — no overlay twin to delete. Highest leverage to de-risk early.
- **5b — `/config` + MAIN: DONE.** `/config` serves `{ localhost, lanUrl, port }`;
  `web/pages/main/{main.html,main.css,main.js}` is the split-pane card; shell + card fetch
  `/config`; the `{{LAN_URL_BLOCK}}`/`MainPage.cs` string-replace path is gone. The full MAIN
  info-box stayed shell chrome and moved with the shell in 5c.
- **5c — the shell: DONE.** `web/shell/{mfd.html,mfd.css,mfd.js}` (~1849 lines: bezel/keys, split logic,
  all `forwardX*` relays, `showPage`, `mfdButton`, indicators, orientation, power-on/boot, the SSE
  relay handler, the info-box markup). `TelemetryServer` serves it from `/`; `serve_web.py` serves
  it from `/`, injects the MAP mock at `/map-view`, serves `/config`, and serves captured
  `/airframe[-layout]` assets for AVN when available.

**Risks / watch-items:** blast radius (MAP=feed, shell=everything → harness-green-first); the
`mapFrame` source guard; ES modules over http if/when shared JS is extracted (step 6 — fine over the
http harness + live server, but the shell is served at `/`).

## Implementation playbook (for an agent continuing this work)

Concrete, learned-by-doing guidance. **WPN is the reference implementation — copy its
pattern.** Read `web/pages/wpn/*` and the WPN-specific hooks in `web/shell/mfd.js` first.

### File map (current, post-step 5c)
```
web/
  shared/  font.css  theme.css  share-tech-mono.woff2   # font.css → /assets/shared/...woff2
  shell/   mfd.html  mfd.css  mfd.js                    # DONE: bezel shell + split/page hosting
  pages/
    wpn/   wpn.html  wpn.css  wpn.js                     # DONE: one file, two profiles
    tgl/   tgl.html  tgl.css  tgl.js                     # DONE: + declarative softkey contract
    tgp/   tgp.html  tgp.css  tgp.js                     # DONE: one profile (feed, like MAP)
    avn/   avn.html  avn.css  avn.js                     # DONE: two profiles (full anchors to bezel geom)
    rwr/   rwr.html  rwr.css  rwr.js                     # DONE: one profile (responsive SVG), 2 streams
    map/   map.html  map.css  map.js                     # DONE: base map + only /stream consumer
    main/  main.html main.css main.js                    # DONE: split-pane card; full MAIN is shell chrome
src/
  TelemetryServer.cs   # ServeAsset (/assets/ route) + ServeAssetRel(ctx,"pages/x/x.html");
                       #   per-page routes (e.g. /wpn) call ServeAssetRel. /assets suffix-matches
                       #   the embedded-resource manifest "<RootNamespace>.web.<dotted path>".
NOXMFD.csproj          # <EmbeddedResource Include="web\**\*" />
.gitattributes         # *.woff2/png/jpg = binary (don't let git mangle EOLs)
```

### The per-page migration recipe (historical; reuse for future pages)
1. **Move the bare page** `XxxPage.cs` → `web/pages/xxx/{xxx.html,xxx.css,xxx.js}`. Link
   `/assets/shared/font.css` + `theme.css` (kills that page's inline font copy). Point its
   route in `TelemetryServer` at `ServeAssetRel(ctx,"pages/xxx/xxx.html")`; delete the `.cs`.
2. **Add the `full` profile** to the same page files when the page has a distinct full layout,
   gated by a `layout:'full'` field in the page's layout message (adds `body.full`). Scope
   full-only CSS under `body.full` so the verified compact layout is untouched. Historically,
   this came from the old shell overlay renderer + CSS; future work should use the current
   `web/shell/mfd.js` hooks as the integration point.
3. **Host full-view in `#page-frame`** (see shell hooks).
4. **Delete the old overlay path** (renderer, markup, element refs/state, CSS) once the iframe
   version is driving full view.

### Shell hooks in `web/shell/mfd.js` (grep these — they are the integration points)
- **`#page-frame`** — the full-view host iframe in the `.screen` recess (after the map iframe).
  CSS: `#page-frame{position:absolute;inset:6px;display:none}`, shown via `.screen.page-on`,
  hidden in `.screen.split`. It sits **below** `.overlay` (so bezel labels paint on top) and
  below `.screen-off` (so power-off blacks it out). `const pageFrame = …`.
- **`showPage(name)`** — for an iframe-hosted page: `screenEl.classList.toggle('page-on', …)`,
  lazy-set `pageFrame.src` once, then forward layout+data+labels. **Set `PAGES.<name>.opaque
  = false`** (the iframe is the opaque content; an opaque overlay would cover the frame).
- **`forwardXToFrame()` functions** — mirror the split `forwardXToPanes` but (a) target
  `pageFrame.contentWindow`, (b) compute **full-screen geometry from the bezel separators**.
  `sepEls = document.querySelectorAll('#keys-left .sep')` indexes as **0 = above key0,
  i+1 = below key i** (7 separators for 6 keys). Map shell-viewport coords into the frame by
  subtracting `pageFrame.getBoundingClientRect().top`. WPN's `forwardWpnLayoutToFrame` is the
  worked example (key-slot spans + an image area).
- **`pageFrame.addEventListener('load', …)`** — re-push the snapshot once the frame loads
  (it may start loading mid-update). Guard `if (splitMode || currentPage !== '<page>') return`.
- **SSE update handler** (the `else if (m.type === 'loadout'|'cm'|…)` chain) — when data
  changes, forward to the frame: `if (currentPage==='<page>' && !splitMode) forwardXToFrame()`.
- **Nav vs softkeys.** Pagination/navigation (MAIN/PREV/NEXT) is **shell state** → keep it
  shell-owned (`placeWpnNavLabels` is the pattern; `mfdButton`'s `wpn-prev/next` cases just
  bump `wpnPage` and call `showPage`). Only **per-item action keys** (TGL's per-target
  deselect) need the softkey contract (section B).
- **`broadcastOrientation()`** forwards `orient` to panes — also forward to `pageFrame` (for
  CSS that keys off `body.portrait/landscape`, e.g. WPN's image rotation).

### The shell⇄page postMessage protocol (envelope: `{ mfd:true, type, … }`)
Shell → page (data **down**): `'<page>'` (the sliced rows + selection), `'<page>-layout'`
(geometry; include `layout:'full'|'compact'` + the slots/bands the page needs), `'cm'`,
`'orient'`. Page → shell (**up**, only where needed): the `'softkeys'` contract (section B).
A page is a pure reactive renderer: it renders to its own container and never knows full-vs-split.

### The softkey contract (landed on TGL — reference for the remaining pages)
**DONE on TGL.** The TGL page emits per-target softkeys `{side, slot, label, action:'target.deselect',
data:{id}}`; the shell's `applyFrameSoftkeys` maps `slot+paneOffset`→physical key, places the
label, and `mfdButton`'s `target.deselect` case dispatches (the shell owns
`sendCommand('target.deselect',{id})`). **Slot range:** the page emits a pane-local **1-based**
row slot; full view maps 1:1 (`paneOffset 0`, row keys 1–5/side; nav on slot 0 stays shell-owned).
**Remaining contract work:** split-mode deselect is **not** on the contract yet — `renderSplitLabels`
still hand-binds `tgl-deselect` (the shell ignores emitted softkeys when `splitMode`). Fold split
onto this path when convenient. `target.select` (MAP tap) and `target.deselect` live in
`todo/write-command-channel.md` + `CommandDispatcher.cs`.

### Verifying without the game (critical — the C# build does NOT check the JS/CSS)
`dotnet build` verifies the C# routes and embedded-resource inclusion, but it never parses the
browser JS/CSS. So **always verify rendering in a browser**. The proven loop, no game required:
1. Edit `web/shell/...` and/or `web/pages/...` → `dotnet build` (verifies the server still builds
   and the embedded-resource manifest is valid).
2. Run the **shell harness over http** (`tools/serve_web.py --open`, or launch.json config `hud-web`):
   serves `web/shell/mfd.html` at `/`, `/<page>`→`web/pages/<page>/<page>.html`, `/assets/*`→`web/*`
   with fallback to `preview/assets/*` captures, `/weapon?…`→captured or mock icons, `/config`, and
   `/airframe[-layout]` captures for AVN. `preview-mock.js` is injected into the MAP page and supplies
   a synthetic or captured frame, so the shell drives the frame end-to-end.
3. Drive it with the Preview MCP: `preview_eval` `window.location.href='http://127.0.0.1:<port>/'`,
   click a bezel key (`[...document.querySelectorAll('.key')].find(k=>k.dataset.action==='wpn').click()`),
   then probe `#page-frame.contentDocument`. **Gotchas:** `preview_screenshot` reliably times
   out here — use `preview_eval` structured probes instead (also the *preferred* way to check
   exact values). The preview viewport can glitch to ~2px wide (everything collapses to width 0)
   — fix with `preview_resize` to e.g. 1280×860, then re-feed.
4. Then verify **in-game** (the user does this): build auto-deploys the DLL to
   `<GameDir>\BepInEx\plugins`; restart the game. Check full view, split view, the *other*
   pages (shared `showPage` is touched), power-off blackout, portrait/landscape.

### Editing the shell — gotchas
- `web/shell/mfd.js` is the single shell source. The `mapFrame` source guard in the message handler
  is critical: telemetry mirror messages should only come from the canonical MAP iframe.
- JS identifiers like `paneWpnPage`, `selWpnPageFull`, and `wpnPage` are shell state, not page files.
  Grep precisely before deleting or renaming.
- `build_preview.py` no longer builds anything; use `tools/serve_web.py --open` for browser checks.

## Open questions / risks

1. **Per-page CSS isolation.** Iframes already give natural style isolation. The remaining check is
   practical: verify full-view iframe sizing after broad shared CSS or shell-frame changes.
2. **Iframe count / perf.** Full view now uses a page iframe over the always-on MAP iframe; split
   already uses multiple iframes. Confirm on the lowest-spec target (tablet) after the shell migration.
3. **Content-type + caching map.** The embedded-resource resolver and suffix lookup are in place;
   cache headers for static assets are still undecided.
4. **Softkey contract for write actions.** `target.deselect` posts to `/command`;
   define whether the shell or the page owns the POST (lean: page emits intent,
   shell dispatches — keeps `/command` knowledge in one place).
5. **Shared JS extraction.** Step 5 intentionally preserved duplicated helpers; step 6 should extract
   `sse-client`, `mfd-protocol`, and `sendCommand` without changing behavior.

## Related

- `todo/write-command-channel.md` — the `/command` channel the softkey contract
  rides for write actions (`target.deselect`).
- `todo/react-client*.md` — the planned external React client; a separate consumer
  of the HTTP API, out of scope for this in-mod refactor (see *Decisions*).
- The split-screen design (Strategy A: iframe per pane) that this unifies around
  shipped already; its `todo/` doc was removed per the done-doc convention. The
  iframe-per-pane model survives in `web/shell/mfd.js` (`applySplitMode`,
  `renderSplitLabels`).
