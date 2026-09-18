# DOC — kneeboard image viewer

## Status

Implemented, browser-verified (`tools/serve_web.py`) — not yet in-game verified. Ticket
[#82](https://github.com/roke77/NOXMFD/issues/82).

## What this replicates

A player-requested "kneeboard": a folder of personal reference images (airport diagrams,
checklists) the pilot can cycle through in-headset, without alt-tabbing to a browser or file
viewer. Two views — an index of file names, and a full-page image with INDX/NEXT/PREV — per the
ticket's own scoped requirements.

## MD-hub integration — DOC as the switch's 6th member

The ticket asked for DOC as "another item in the MD page hub" — joining AKF/MIS/OBJ/BDF/PAL's
direct switch (each carries the other siblings as a nav-row entry, `mark` on whichever is current —
see [docs/md-pages.md](md-pages.md)). The obstacle: `split-slots.js`'s `SPLIT_SLOTS.akf/mis/obj/
bdf/pal` each consumed exactly all 6 of a split pane's physical nav slots (3 left + 3 right),
placing their (then) 6 `NAV[page]` items 1:1 — a 6th sibling pushes that to 7, past the budget.

First shipped as a MAIN-level destination instead (next to MD, not in the switch) to avoid touching
those five pages — but that traded away the direct AKF↔DOC switch the ticket actually asked for,
and adding a 13th item to MAIN's own already-full 12-slot list (`NAV.main` + `BEZEL_EXTRAS.main`)
silently dropped WPN off the end of it. Reverted once both problems surfaced: DOC is the switch's
6th member after all (`NAV.akf`/`NAV.mis`/`NAV.obj`/`NAV.bdf`/`NAV.pal` each gain a trailing `{
label: 'DOC', action: 'doc' }`; `NAV.doc` carries the same six-item switch plus its own
INDX/NEXT/PREV, 10 items total), and MAIN goes back to its original 12.

The 6-to-7-item overflow is real and still had to be solved: `mfd.js`'s `renderSplitLabels` now
paginates this whole group in a split pane (`mdPaneSlice`/`paneMdPage`, `MD_GROUP_PAGES` — `md-prev`/
`md-next` bump the shared page index, same shape as MAIN's own `main-prev`/`main-next`), and
`SPLIT_SLOTS` no longer declares fixed slots for any of the six pages (`split-slots.test.js`'s
`NO_SPLIT_TABLE` documents the exclusion, same as MAIN/MAP). Full view (the generic index-into-12
sweep in `showPage`) and F-35's glass (`itemsFor`'s generic `NAV[page]` fallback, 12-slot `edge`
grid) needed **no** equivalent change — both already place an arbitrary-length list generically, and
7 (or DOC's own 10) comfortably fits without special-casing.

`INDX`/`NEXT`/`PREV` act on the page in place rather than naming a destination — same shape as
RDR/HSD's own `R+`/`R-`/`MODE` (`layout-coverage.test.js`'s `BEHAVIOURS` list, not
`CLASSIC_FULL`/`CLASSIC_SPLIT`/`F35`); `md-prev`/`md-next` don't even need that — like
`main-prev`/`main-next`, they're synthesized only inside the split-pane renderer, never a `NAV`
entry.

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
