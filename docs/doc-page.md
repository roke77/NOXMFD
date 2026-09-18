# DOC — kneeboard image viewer

## Status

Implemented, browser-verified (`tools/serve_web.py`) — not yet in-game verified. Ticket
[#82](https://github.com/roke77/NOXMFD/issues/82).

## What this replicates

A player-requested "kneeboard": a folder of personal reference images (airport diagrams,
checklists) the pilot can cycle through in-headset, without alt-tabbing to a browser or file
viewer. Two views — an index of file names, and a full-page image with INDX/NEXT/PREV — per the
ticket's own scoped requirements.

## MD-hub integration — why DOC is its own MAIN destination, not a 6th switch arm

The ticket originally asked for DOC as "another item in the MD page hub," i.e. joining
AKF/MIS/OBJ/BDF/PAL's direct switch (each carries the other four as a nav-row entry, `mark` on
whichever is current — see [docs/md-pages.md](md-pages.md)). That switch can't simply grow a 6th
member: `split-slots.js`'s `SPLIT_SLOTS.akf/mis/obj/bdf/pal` each already consume all 6 of a split
pane's physical nav slots (3 left + 3 right) placing their existing 6 `NAV[page]` items 1:1. A 6th
sibling would push every one of those five pages from 6 items to 7, overflowing that budget and
requiring split-pane pagination to be retrofitted onto five already-shipped, tested pages just to
fit DOC in.

Resolved (user-confirmed) as: DOC gets its own top-level MAIN destination — `BEZEL_EXTRAS.main` /
`f35.js`'s `MAIN_EXTRAS`, next to the existing MD button — same shape as RDR/AFM/SQD. `NAV.doc` is
just `MAIN, INDX, NEXT, PREV` (4 items, comfortably inside every layout's budget). AKF/MIS/OBJ/
BDF/PAL are untouched. The cost: reaching DOC from AKF (or vice versa) is DOC → MAIN → AKF, one
extra hop, rather than a direct switch — accepted as the simpler, lower-risk shape.

`INDX`/`NEXT`/`PREV` act on the page in place rather than naming a destination — same shape as
RDR/HSD's own `R+`/`R-`/`MODE` (`layout-coverage.test.js`'s `BEHAVIOURS` list, not
`CLASSIC_FULL`/`CLASSIC_SPLIT`/`F35`).

## Data model

Unlike every other `CapturedAssetEndpoint` asset (map image, unit/weapon icons, airframe
silhouettes), DOC's images are never embedded in the DLL or captured in-process — the player drops
them onto disk themselves, since the whole point is a personal, player-curated set. `DocEndpoint.cs`
owns a dedicated folder, `BepInEx/plugins/NOXMFD/kneeboard/` (created on first use), separate from
the shared `BepInEx/plugins/` root `CapturedAssetEndpoint.ServeMap` falls back to for `map.png` —
DOC's folder can hold many files without cluttering that shared root.

- `GET /doc-list` — the live `.png`/`.jpg`/`.jpeg` listing, filesystem/OS order (no explicit
  sort), re-read from disk on every request rather than cached, so a file the player adds or
  removes mid-session just starts/stops appearing.
- `GET /doc-image?name=` — serves one file's bytes. `name` is matched by exact, case-sensitive
  string equality against that same request's own `ListImages()` call (`DocNameMatch.Find`, its own
  pure file so it can be unit-tested in `tools/tests/DocNameMatchTests.cs` without pulling in
  `BepInEx.Paths`/`Plugin.Log`) — never `Path.Combine`'d with the raw query value — so a
  path-traversal name (`../../secrets.png`) simply doesn't match anything and 404s rather than
  resolving outside the kneeboard folder.

`doc.js` re-fetches `/doc-list` on INDX and on every NEXT/PREV (not cached client-side either, for
the same reason); the wrap-around stepping itself is a pure function, `doc-cycle.js`
(`doc-cycle.test.js`).
