# src/ web-frontend architecture — design & refactor plan

Status: **in progress** — steps 1–3 done, **step 4 underway (TGL migrated)**. Resource
plumbing, shared font/theme, **WPN** as the proof page, then **TGL** — one file, two layout
profiles, full view hosted in an iframe, overlay deleted, and the **declarative softkey
contract** introduced on TGL (full view; split deselect still on the legacy binding).
Remaining: step 4's TGP/AVN/RWR/MAIN; then the shell + MAP (step 5); then shared JS (step 6)
+ the preview rework (step 7).

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

## Current state (the problem, with evidence)

> **Note (historical baseline):** this section describes the *pre-migration* state and its
> line numbers are from before any work landed. WPN is now migrated (see the Migration plan
> and the **Implementation playbook** below); treat the line numbers here as approximate
> history, and `grep` for the real current locations.

Three layers of duplication, all stemming from "HTML lives in C# strings":

### 1. Everything is a `const string Html` blob
Each page is `internal static class XxxPage { public const string Html = """ …
HTML + <style> + <script> … """; }`, served by `TelemetryServer` via `ServePage`
(`TelemetryServer.cs:279-306`). No highlighting, no linting, no formatting, no
reuse. Editing requires `tools/build_preview.py` to extract the blob back out to
a browser-testable file. Sizes today:

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
`tools/build_preview.py` currently extracts `const string` blobs → `preview/*.html`
and injects `tools/preview-mock.js`. After the move, the real `web/` files are
directly servable, so the preview tool **simplifies** to: serve `web/` over
`http.server` with the mock layer for `/stream`, `/map`, `/icon`, etc. No more
C#-string extraction step. (The "run `build_preview.py` after editing the
frontend" workflow rule becomes "just refresh".)

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
   the contract is the remaining contract work. **TGP, AVN, RWR, MAIN still to do.**
5. **Convert the shell itself** (`MfdPage.cs` → `web/shell/mfd.*`) and **MAP**
   (`ClientPage.cs` → `web/pages/map.*`) once the page pattern is proven.
6. **Extract shared JS** (`sse-client`, `mfd-protocol`, `sendCommand`) and
   de-duplicate the now-parallel copies across pages.
7. **Simplify `build_preview.py`** to serve `web/` directly; update the workflow
   note + memory.

## Implementation playbook (for an agent continuing this work)

Concrete, learned-by-doing guidance. **WPN is the reference implementation — copy its
pattern.** Read `web/pages/wpn/*` and the WPN-specific hooks in `MfdPage.cs` first.

### File map (current, post-WPN)
```
web/
  shared/  font.css  theme.css  share-tech-mono.woff2   # font.css → /assets/shared/...woff2
  pages/
    wpn/   wpn.html  wpn.css  wpn.js                     # DONE: one file, two profiles
    tgl/   tgl.html  tgl.css  tgl.js                     # DONE: + declarative softkey contract
src/
  TelemetryServer.cs   # ServeAsset (/assets/ route) + ServeAssetRel(ctx,"pages/x/x.html");
                       #   per-page routes (e.g. /wpn) call ServeAssetRel. /assets suffix-matches
                       #   the embedded-resource manifest "<RootNamespace>.web.<dotted path>".
  MfdPage.cs           # the shell (still a const-string blob). Hosts pages; see hooks below.
  ClientPage.cs        # MAP page (/map-view) — still a blob; migrate in step 5.
  {Avn,Tgp,Rwr,Main}Page.cs   # still blobs (split-only bare pages) + overlay twins in MfdPage
NOXMFD.csproj          # <EmbeddedResource Include="web\**\*" />
.gitattributes         # *.woff2/png/jpg = binary (don't let git mangle EOLs)
```

### The per-page migration recipe (what WPN + TGL did — repeat for TGP, AVN, RWR, MAIN)
1. **Move the bare page** `XxxPage.cs` → `web/pages/xxx/{xxx.html,xxx.css,xxx.js}`. Link
   `/assets/shared/font.css` + `theme.css` (kills that page's inline font copy). Point its
   route in `TelemetryServer` at `ServeAssetRel(ctx,"pages/xxx/xxx.html")`; delete the `.cs`.
2. **Add the `full` profile** to the same page files, gated by a `layout:'full'` field in the
   page's layout message (adds `body.full`). Scope full-only CSS under `body.full` so the
   verified compact layout is untouched. Transcribe the full layout from the overlay renderer
   + CSS in `MfdPage.cs`.
3. **Host full-view in `#page-frame`** (see shell hooks). 4. **Delete the overlay** (renderer,
   markup, element refs/state, CSS) from `MfdPage.cs`.

### Shell hooks in `MfdPage.cs` (grep these — they are the integration points)
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

### Verifying without the game (critical — the C# build does NOT check the JS)
`dotnet build` only validates the C# **raw-string-literal integrity** (an accidental `"""` or
broken literal fails it); it never parses the embedded JS/CSS. So **always verify rendering in
a browser**. The proven loop, no game required:
1. Edit `web/pages/...` and/or `MfdPage.cs` → `dotnet build` (catches literal breakage).
2. `python tools/build_preview.py` → regenerates `preview/index.html` from the edited shell.
3. Run the **shell harness over http** (`tools/serve_web.py`, or launch.json config `hud-web`):
   serves `preview/index.html` at `/`, `/<page>`→`web/pages/<page>/<page>.html` (any migrated
   page), `/assets/*`→`web/*`, `/weapon?…`→a mock SVG, and everything else from `preview/` (so
   `map-view.html` etc. + the injected `preview-mock.js` resolve). `preview-mock.js` supplies a
   6-weapon loadout + CM + target list (with ids), so the shell drives the frame end-to-end.
4. Drive it with the Preview MCP: `preview_eval` `window.location.href='http://127.0.0.1:<port>/'`,
   click a bezel key (`[...document.querySelectorAll('.key')].find(k=>k.dataset.action==='wpn').click()`),
   then probe `#page-frame.contentDocument`. **Gotchas:** `preview_screenshot` reliably times
   out here — use `preview_eval` structured probes instead (also the *preferred* way to check
   exact values). The preview viewport can glitch to ~2px wide (everything collapses to width 0)
   — fix with `preview_resize` to e.g. 1280×860, then re-feed.
5. Then verify **in-game** (the user does this): build auto-deploys the DLL to
   `<GameDir>\BepInEx\plugins`; restart the game. Check full view, split view, the *other*
   pages (shared `showPage` is touched), power-off blackout, portrait/landscape.

### Editing `MfdPage.cs` (a ~3000-line `"""…"""` blob) — gotchas
- For **large block deletions** use `sed -i 'A,Bd'` after confirming the exact boundaries with
  `sed -n`; for small/anchored changes use `Edit`. **After any `sed`/external change you must
  re-`Read` before `Edit`** (the tool guards on a stale read). `git mv` also resets read state.
- JS identifiers like `paneWpnPage`, `selWpnPageFull`, `wpnPage` contain "Wpn" but are **not**
  the C# `WpnPage` class — grep precisely before deleting anything.
- When deleting a page's overlay: remove the renderer(s), the markup, the element `const`s +
  state vars, **and** the CSS — but **trim shared rules, don't drop them** (e.g. the WPN/TGL
  `.page-ind` rule, and the `.screen.split > .overlay > .<x>-panel` hide list). Keep `xxxData`
  / `xxxPage` state the forwarders still use.
- `build_preview.py` guards each page with `if XXX.exists()`; when a page's `.cs` is deleted,
  remove its `XXX`/`OUT_XXX`/`/xxx?bare` references there too (its file:// preview returns with
  the step-7 rework). CRLF warnings on `git commit` are benign.

## Open questions / risks

1. **Per-page CSS isolation.** Iframes already give natural style isolation — good.
   But the full-view single iframe must fill the recess exactly like the overlay
   did; verify no regressions in sizing/letterbox vs the current overlay.
2. **Iframe count / perf.** Full view goes from "overlay (no iframe)" to "one
   iframe". MAP already runs an iframe in full view with no issue, and split runs
   two; one more is negligible. Confirm on the lowest-spec target (tablet).
3. **Content-type + caching map.** Need a small, explicit path→(resource,mime)
   resolver in `TelemetryServer`; decide on cache headers for static assets.
4. **`{{LAN_URL_BLOCK}}` & other server-injected bits.** Currently string-replaced
   into `MfdPage.Html`/`MainPage.Html`. Decide: keep a tiny replace pass, or move
   to a runtime `/config` JSON the shell fetches.
5. **Softkey contract for write actions.** `target.deselect` posts to `/command`;
   define whether the shell or the page owns the POST (lean: page emits intent,
   shell dispatches — keeps `/command` knowledge in one place).
6. **ES modules over `file://`.** The preview serves over `http.server` (fine for
   modules); just ensure nothing assumes `file://`.

## Related

- `todo/write-command-channel.md` — the `/command` channel the softkey contract
  rides for write actions (`target.deselect`).
- `todo/react-client*.md` — the planned external React client; a separate consumer
  of the HTTP API, out of scope for this in-mod refactor (see *Decisions*).
- The split-screen design (Strategy A: iframe per pane) that this unifies around
  shipped already; its `todo/` doc was removed per the done-doc convention. The
  iframe-per-pane model survives in `MfdPage.cs` (`applySplitMode`,
  `renderSplitLabels`).
