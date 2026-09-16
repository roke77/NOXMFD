# Shared telemetry connection for MAP panes/portals

A player reported that opening a third MAP instance (classic split pane or F-35 portal) left its
map image and some icons never loading, while telemetry markers still moved. Root cause: HTTP/1.x
browsers allow only ~6 simultaneous connections per origin, and every MAP-family document
(`map.js`, and F-35's always-on `telemetry-tap.js`) opened its own permanent `EventSource('/stream')`
— a connection that never releases for the life of the document. A handful of MAP instances,
classic split panes plus the F-35 telemetry tap plus any TGP feeds, can exhaust that budget, so
the map/icon image requests are left queued indefinitely rather than failing outright (map.js's
`onerror`-only retry never even engages for a request that's merely queued, not failed).

## What changed

Every MAP-family window/glass already has exactly one **permanent, connection-owning** document
that never closes while the shell is open: classic full view's `mapFrame` iframe, or F-35's
`telemetry-tap.js` tap. Any *additional* MAP pane/portal a pilot opens on top of that was always
redundant — same server, same data, its own connection.

`TelemetrySource._emit`/`_onMessage` (`src/web/services/telemetry-source.js`) already derives and
posts every other page's slice up to whichever shell hosts it (avn/wpn/rwr/... — see its own header
comment); this now also posts the **raw frame itself**, tagged `map-frame`, the same way. Both
shells already have a generic relay mechanism for forwarding a cached slice to whichever pane/portal
currently shows the matching page (`mfd.js`'s `RELAY_MESSAGES`/`forwardToPanes`, `f35.js`'s
`PAGE_FEEDS`/`forwardSlice`) — `map-frame` is simply registered into both, the same as any other
slice, requiring no new plumbing beyond that registration.

A MAP pane/portal loaded with `?relay=1` (`layout-pages.js`'s `CLASSIC_SPLIT.map`/`F35.map`) never
calls `TelemetrySource.connect()` — it renders from the shell-relayed `map-frame` messages via the
same `renderFrame`/`handleNoMission` functions owner-mode already drives locally, so those functions
don't know or care which path fed them. `mapFrame`'s own hardcoded src and standalone/bare direct
access (e.g. an OBS overlay pointed straight at `/map-view?bare`) are unaffected — they still own
their connection exactly as before.

SOI (cid/pane focus) and cursor/action forwarding needed no change: they already route through the
shell by pane index/iframe identity, sourced from whichever document owns the connection — a
secondary pane never participated in deriving that state, only in receiving it.

## Out of scope

TGP's `multipart/x-mixed-replace` MJPEG feed (`TgpMjpegHandler.cs`) is a separate long-lived
connection per viewer and holds the same kind of slot, but each TGP viewer can show a different
camera state — sharing one feed across viewers is a materially different problem, not addressed
here.

## Verification

- [ ] Classic split with both panes on MAP: confirm both render (image + icons), and that only
  `mapFrame`'s connection appears in `/soi-instances` (`SseHub`) — the panes should register no
  connection of their own.
- [ ] F-35 with 3+ portals on MAP: same check against the tap's one connection.
- [ ] SOI cycling still frames and drives the correct MAP pane/portal's cursor.
- [ ] FOLLOW/GRID chips still reflect each pane's own state independently.
- [ ] A pane/portal switched onto MAP after the mission is already running catches up immediately
  (relies on the owner's most recent frame reaching it, not a fresh connection's own first frame).
