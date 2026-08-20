# Splitting README into `man/` — per-feature user manuals

## Status

Planning — not started. Branch `docs-manuals`.

## The problem

`README.md` is 468 lines. Two sections carry almost all of that weight:

- **MFD pages** (~150 lines) — 13 pages, each a paragraph of prose plus a collapsed screenshot.
  MD alone (AKF/BDF/PAL/MIS/OBJ folded into one bullet) is a single ~9-sentence paragraph.
- **Extended Keybinds** (~90 lines) — 15+ keybind groups, then three more full subsections (SOI,
  PAD cursor, Immersion Options) nested under it.

Both are accurate and useful, just in the wrong place: a landing-page README should be skimmable
in a couple of minutes, and right now finding "what does R+/R− do on WPT" means scrolling past
twelve other pages' worth of detail first. This doc plans splitting that depth out, not doing it —
see "Open questions" below for what still needs deciding before the split itself starts.

## Where things live after the split

- **`man/`** (new, top-level, sibling to `docs/`) — end-user feature manuals: description,
  screenshots, step-by-step instructions, short tutorials. This is the audience README already
  serves; the split doesn't change who it's for, only how much of it sits on the landing page.
- **`docs/`** (unchanged) — stays exactly what it already is: developer-facing design/investigation
  record (decompiled game internals, the "why" behind a shape, in-game verification notes). Not
  user-facing, not part of this split.
- **`README.md`** — "MFD pages" and "Extended Keybinds" shrink to a one- or two-sentence summary
  per item plus a link into the matching `man/` page. Screenshots move with their content; README
  keeps at most a hero image.

## Granularity — resolved

One manual per real NAV destination (`src/web/shell/nav-model.js`'s `NAV` object — the ground truth,
not README's own grouping, which is part of what made README's sections inconsistent: some NAV
pages got their own bullet, some got folded into another's paragraph). Two deliberate merges:

- **BDF + PAL → one manual.** Same underlying page (`bdf.js` doubles as PAL via `?pal`); two
  manuals would just describe identical content with one flag different.
- **MFD shell (HIDE/FULL/PIN/SWAP/F_VIEW/H_SPLIT/V_SPLIT/V_WIDE_SPLIT) → folds into the LYT manual,
  not its own file.** That chrome is CLASSIC's own bezel, not a NAV page — LYT's manual becomes the
  one place that explains every layout that exists (CLASSIC, with its bezel, and F-35, with its
  portals/corner grips), so a pilot picking a layout there finds out how to drive it in the same
  doc rather than being sent to a third page.

Immersion Options stays a section inside the KEY manual, per the earlier "KEY includes everything"
call — it's config that lives at the bottom of the KEY page itself.

## The 19 documents

| # | File | Covers |
|---|------|--------|
| 1 | `man/main.md` | MAIN |
| 2 | `man/map.md` | MAP |
| 3 | `man/wpt.md` | WPT (MAP sub-page) |
| 4 | `man/avn.md` | AVN |
| 5 | `man/afm.md` | AFM |
| 6 | `man/rwr.md` | RWR |
| 7 | `man/rdr.md` | RDR |
| 8 | `man/tgp.md` | TGP |
| 9 | `man/tgt.md` | TGT |
| 10 | `man/wpn.md` | WPN |
| 11 | `man/ext.md` | EXT hub (end-user side; building one stays in `EXTENSIONS.md`) |
| 12 | `man/akf.md` | AKF (MD group) |
| 13 | `man/mis.md` | MIS (MD group) |
| 14 | `man/obj.md` | OBJ (MD group) |
| 15 | `man/bdf.md` | BDF **and** PAL (MD group, same page) |
| 16 | `man/hud.md` | HUD (CFG group) |
| 17 | `man/keybinds.md` | KEY (CFG group) — binds, SOI, PAD cursor, Immersion Options, all as sections in one doc |
| 18 | `man/rates.md` | RTS (CFG group) |
| 19 | `man/layouts.md` | LYT (CFG group) — CLASSIC (incl. its bezel/shell chrome) and F-35, both layouts fully explained |

## Open questions

Still to decide before starting the actual split:

- **Naming/layout** — `man/<page>.md` flat (as listed above), or `man/pages/<page>.md` mirroring
  `src/web/pages/`'s own layout.
- **All at once or incrementally** — split every section in one pass, or start with the longest
  page entries (MD group, MAP, WPT, KEY) and leave short ones (RWR, TGP) inline until the split
  proves worth it there too. A partial split leaves README's own section inconsistent (some
  bullets short-with-link, others still the full paragraph) — leans toward doing it in one pass,
  but worth confirming before committing to the larger diff.
- **Link upkeep** — nothing today checks that a doc link actually resolves (`layout-coverage.test.js`
  is the closest precedent, but for NAV destinations, not markdown links). Decide whether this is
  worth a lightweight check or just manual care, given how infrequently these pages change.

## Scope

- [ ] Resolve the remaining open questions above
- [ ] `man/` folder created
- [ ] All 19 manuals written (table above)
- [ ] `README.md`'s "MFD pages" and "Extended Keybinds" sections rewritten to short summaries + links
- [ ] Every new README link verified to resolve
