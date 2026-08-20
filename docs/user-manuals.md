# Splitting README into `man/` — per-feature user manuals

## Status

Planning — not started. Branch `docs-manuals`.

## The problem

`README.md` is 468 lines. Two sections carry almost all of that weight:

- **MFD pages** (~150 lines) — 13 pages, each a paragraph of prose plus a collapsed screenshot.
  MDT alone (AKF/BDF/PAL/MIS/OBJ folded into one bullet) is a single ~9-sentence paragraph.
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

## Open questions

Decide before starting the actual split:

- **Granularity** — one manual file per MFD page (13 files), or grouped by area (e.g. one file for
  MDT's five sub-pages, since README already treats them as one bullet)? Same question for
  Extended Keybinds: one file per keybind family, or one manual covering the whole KEY page
  (binds + SOI + PAD cursor + Immersion Options) the way the page itself presents them together.
- **Naming/layout** — `man/<page>.md` flat, or `man/pages/<page>.md` + `man/keybinds/<topic>.md`
  mirroring `src/web/pages/`'s own layout.
- **Screenshots** — move out of `docs/images/` into `man/` alongside the manual that uses them, or
  stay centralized and get linked from wherever needs them.
- **MFD shell / MFD layouts / Immersion Options** — each currently a mid-sized README subsection.
  Fold into an existing manual (Immersion Options under the KEY manual, MFD shell/layouts under a
  general "using the display" manual) or give each its own file.
- **All at once or incrementally** — split every section in one pass, or start with the three
  longest page entries (MDT, MAP, WPT) and leave short ones (RWR, TGP) inline until the split
  proves worth it there too. A partial split leaves README's own section inconsistent (some
  bullets short-with-link, others still the full paragraph) — leans toward doing it in one pass,
  but worth confirming before committing to the larger diff.
- **Link upkeep** — nothing today checks that a doc link actually resolves (`layout-coverage.test.js`
  is the closest precedent, but for NAV destinations, not markdown links). Decide whether this is
  worth a lightweight check or just manual care, given how infrequently these pages change.

## Scope

- [ ] Resolve the open questions above
- [ ] `man/` folder structure created
- [ ] One manual per MFD page area (MAIN, AFM, AVN, CFG, HUD, MAP, MDT, RDR, RTS, RWR, TGP, TGT, WPN, WPT)
- [ ] Manual(s) for Extended Keybinds (binds, SOI, PAD cursor, Immersion Options)
- [ ] Manual for MFD shell / MFD layouts
- [ ] `README.md`'s "MFD pages" and "Extended Keybinds" sections rewritten to short summaries + links
- [ ] Every new README link verified to resolve
