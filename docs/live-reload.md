# Live reload for `tools/serve_web.py`

## Status

Built, per the proposed shape below.

## The problem

`tools/serve_web.py` already serves `src/web/` fresh on every request — no in-process caching, so a
saved edit is available the instant the next request hits the server. What's missing is the browser
*asking* for that next request: today, after editing a page, verifying it means alt-tabbing to the
browser and hitting refresh by hand, every time, for every edit. This is pure manual toil during an
edit/verify loop that can happen dozens of times per session (CLAUDE.md's own "Build & preview"
section already mandates verifying every frontend edit in this harness).

## Design decisions

**Poll, don't push.** `serve_web.py`'s `H` class is a synchronous `http.server.SimpleHTTPRequestHandler`
under a `ThreadingTCPServer` — holding an SSE connection open per browser tab is possible (one thread
per connection, same as any other request), but adds a persistent-connection lifecycle to reason
about (reconnect on server restart, cleanup on tab close) for a dev-only convenience tool where a
~500ms-1s reload latency is unnoticeable. A `fetch()` poll from the browser is simpler, stateless on
the server, and needs no new machinery — the pragmatic choice here, not the "correct" one in the
abstract.

**mtime signature, not content hash.** Detecting "something changed" only needs a cheap comparable
value, not proof of exactly what changed. Walking `src/web/` (plus `tools/preview-mock.js`, since
`_map_page()` already treats it as part of what a browser sees) and taking `max(mtime for every
file)` is a handful of `os.stat()` calls — sub-millisecond at this repo's size — computed fresh per
request as this file's other mock endpoints already do (`_hud_options()`, `_config()`, etc. all
rebuild their response from scratch per call; no exception needed here).

**Inject into the two shell pages only, not into every page/`</head>`.** `_map_page()` and
`_wpt_page()` already establish the injection pattern this reuses — a `<script>` spliced before
`</head>` in a page built fresh per request (see `serve_web.py:163`-`179`). But those two are
iframe-hosted content; the reload watcher belongs on the two **top-level shell pages** (`/` →
`mfd.html`, `/f35` → `f35.html`) only. A full top-level `location.reload()` already re-fetches every
child iframe (the map iframe, `#page-frame`, split panes) with it — injecting the same watcher into
every individual page would mean nested/duplicate reload triggers with no added benefit.

**Known limitation, by design: a standalone page route opened directly won't auto-reload.**
`/wpn`, `/tgt`, or any other `/pages/<name>/<name>.html` route hit directly in a browser tab
(rather than through `/` or `/f35`) has no watcher on it — only the two shell pages get one. Editing
that page's file still needs a manual refresh in that scenario. Worth stating explicitly rather than
letting a future session assume every route auto-reloads; the fix, if this ever matters in
practice, is opening the page through the shell instead of standalone.

**Never touches the real embedded plugin.** Like the mock injection this reuses, the watcher script
lives entirely in `serve_web.py`'s response-building code, not in `src/web/shell/*/*.html` source.
The real mod DLL (which embeds those HTML files as resources, per `docs/src-architecture.md`) never
sees this script — it only exists in the harness's own served bytes.

**Editing `serve_web.py` itself isn't covered**, and can't be with this design — it's the running
process; a change to it needs a manual restart same as today. Worth a one-line callout in the
injected page (or just accepted as a known limitation) rather than solved — restarting the harness
after touching `serve_web.py` is already the existing habit. Hit for real while building this: a
mock function (`_rates_config_merged()`) was edited while an old server instance was still running,
and it kept serving the stale response until manually killed and restarted.

**A capture refresh does reload, but only the active capture.** `_reload_token()` includes the
`CURRENT` pointer file and the currently-active `preview/captures/<name>/` folder's own contents,
so re-running `capture_assets.py` (new icons/map.jpg) or switching which capture `CURRENT` points
at both trigger a reload — but the rest of the capture library is intentionally excluded from the
mtime scan (those folders aren't served, so their mtimes are noise, and the library can grow to
many dated folders over time).

**Not a live-reload bug, but easy to mistake for one: split vs. full-view CSS are different
files for what looks like the same box.** `main.html`/`main.css` only render `/main?bare` — the
split-pane iframe. Full-view MAIN (`/`) is separate shell chrome baked into
`src/web/shell/classic/mfd.html`/`mfd.css`. The watcher reloads correctly either way; editing the
wrong one just has nothing to show in the view you're looking at.

## Proposed shape

1. **New endpoint** `GET /__reload-token` — walks `WEB` (and `MOCK`), returns `max(mtime)` as a plain
   string or small JSON body. Mirrors the existing mock-endpoint style (`_config`, `_hud_options`)
   exactly; no new pattern.
2. **New injection function** `_shell_page(fp)`, mirroring `_map_page()`/`_wpt_page()`'s shape:
   reads the shell HTML fresh, splices a small `<script>` before `</head>` that:
   - fetches `/__reload-token` on load and remembers the value,
   - polls it every ~750ms,
   - calls `window.location.reload()` the first time the value differs from what was remembered.
3. **Wire `/` and `/f35`** (`serve_web.py:522`-`525`) through `_shell_page()` instead of the current
   direct `self._file(...)` call.
4. No config flag needed to disable it — this is a dev-only harness; the watcher is inert (one small
   poll, no visible effect) whenever nothing is being edited, and `capture_screenshots.py`'s scripted
   runs don't edit files mid-run, so there's no reload-vs-automation conflict to guard against.

Roughly a dozen lines of Python (the endpoint) and a dozen lines of vanilla JS (the injected script)
— no new dependency, no new pattern, reuses the exact injection mechanism this file already has.

## Scope

- [x] Add `GET /__reload-token` returning `max(mtime)` across `src/web/` + `tools/preview-mock.js`
- [x] Add `_shell_page(fp)` following `_map_page()`'s shape, with the poll-and-reload `<script>`
- [x] Wire `/` and `/f35` through it
- [x] Verify: edit a page's `.js`/`.css`/`.html` while the harness is open, confirm the browser
      reloads within ~1s with no manual refresh
- [x] Verify `capture_screenshots.py`'s Playwright-driven runs are unaffected (no file edits happen
      mid-run today, so this should be a non-issue, but worth one confirming run)
