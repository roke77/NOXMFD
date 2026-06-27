# src/ web-frontend architecture — design & refactor plan

Status: **in progress** — steps 1–3 done (resource plumbing, shared font/theme, and
**WPN fully migrated** as the proof page: one file, two layout profiles, full view hosted
in an iframe, overlay deleted). Steps 4–7 remain (roll to the other pages, starting with
TGL + the softkey contract; then the shell + MAP; then shared JS + the preview rework).

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
    map.html  map.css  map.js     # was ClientPage.cs
    wpn.html  wpn.css  wpn.js     # was WpnPage.cs (now full + split)
    tgl.* tgp.* avn.* rwr.* main.*
```
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
   drop the overlay, host full-view in `#page-frame`. **TGL introduces the declarative
   softkey contract** (page emits `{side,slot,label,action,data}`; shell maps `slot+paneOffset`),
   bringing `target.deselect` onto it and removing its current double-binding.
5. **Convert the shell itself** (`MfdPage.cs` → `web/shell/mfd.*`) and **MAP**
   (`ClientPage.cs` → `web/pages/map.*`) once the page pattern is proven.
6. **Extract shared JS** (`sse-client`, `mfd-protocol`, `sendCommand`) and
   de-duplicate the now-parallel copies across pages.
7. **Simplify `build_preview.py`** to serve `web/` directly; update the workflow
   note + memory.

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
