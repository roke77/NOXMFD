# RDR hub — FCR and HSD pages

## Status

**Shipped in 0.32.0.** Merged to `main` from branch `rdr-fcr-hsd` for
[issue #60](https://github.com/roke77/NOXMFD/issues/60), and README's MFD pages list now reflects
RDR as the FCR/HSD hub.

**Update:** HSD is no longer read-only. It answers this doc's own open question ("Should HSD
become selectable...") — yes, reusing FCR/TGT's target set exactly as FCR's own cursor does:
`hsd` joined `PAD_CURSOR_PAGES` (mfd.js, f35.js), and `hsd.js` gained the same acquisition-gate
cursor, hit-test, and Select-toggles-lock behavior as `rdr.js`'s (see "Interaction" below, kept as
originally written since the design it describes for FCR now applies to HSD unchanged too).

The current `RDR` page is a radar-style B-scope with own-radar contacts, datalink-only contacts,
lock markers, pitbull missile markers, range stepping, and the PAD acquisition cursor. This ticket
splits that surface into two sibling pages:

- **FCR** — Fire Control Radar. Keeps the current RDR page's B-scope behavior and visual design.
- **HSD** — Horizontal Situation Display. Adds a 360-degree situational-awareness picture around
  the player aircraft, focused on datalink-provided aerial contacts.

`RDR` becomes the hub group name, not the name of the sensor picture itself.

## Goal

The pilot should be able to enter the existing RDR destination and switch between two radar-family
views without going back to MAIN:

- **FCR** answers "what does my own fire-control radar see in front of me?"
- **HSD** answers "what aerial picture does the datalink give me around my aircraft?"

FCR should be highlighted while the current B-scope page is displayed. HSD should be highlighted
while the new 360-degree page is displayed.

## Navigation model

Use the same sibling-page pattern as the MD and HUD/KEY/LYT groups:

```js
NAV.rdr = [
  { label: 'MAIN', action: 'main' },
  { label: 'FCR',  action: 'rdr', mark: true },
  { label: 'HSD',  action: 'hsd' },
  { label: 'R+',   action: 'rng-in' },
  { label: 'R-',   action: 'rng-out' },
]

NAV.hsd = [
  { label: 'MAIN', action: 'main' },
  { label: 'FCR',  action: 'rdr' },
  { label: 'HSD',  action: 'hsd', mark: true },
  { label: 'R+',   action: 'rng-in' },
  { label: 'R-',   action: 'rng-out' },
  { label: 'MODE', action: 'hsd-mode' },   // CEN<->DEP toggle — see "CEN/DEP display modes" below
]
```

Keep action `rdr` for FCR so existing routes, shell dispatch, focused-cursor behavior, and saved
layouts continue to land on the inherited page. Add action `hsd` for the new page. HSD's R+/R- use
the SAME `rng-in`/`rng-out` action names FCR's own do (not a separate `hsd-range-in`/`-out` pair) —
the shell dispatches both identically regardless of which page is showing (mfd.js/f35.js), so one
name pair covers both.

The R+/R- row remains a page-local range rocker. FCR keeps the current radar-range stepping. HSD
gets its own range stepping because its scale is a real circular map range, not a fraction of a
radar cone.

### Layout placement

- Classic full view: add `hsd` to `LayoutPages.CLASSIC_FULL`.
- Classic split view: add `hsd` to `LayoutPages.CLASSIC_SPLIT`.
- F-35 glass: add `hsd` to `LayoutPages.F35`.
- Classic split slots: add slots for `NAV.rdr`'s five entries and `NAV.hsd`'s six (the extra one is
  its own MODE key). Keep FCR/HSD adjacent.
- F-35 edge grid: keep the FCR/HSD mark lights from the `NAV` table; add a RANGE decorator between
  the HSD R+/R- entries if the labels stay adjacent there.
- PAD cursor focus: `hsd` is in `PAD_CURSOR_PAGES` — contact selection (Select toggles a lock) is
  live from the start; no cursor-driven panning (HSD keeps its own R+/R- range stepping instead).

## Page split

FCR should be the current page, mechanically:

- keep `/rdr`;
- keep `src/web/pages/rdr/rdr.html`, `.css`, `.js`, and `rdr.test.js`;
- change visible page text and comments from generic RDR where useful to FCR;
- preserve the existing `rdr` postMessage contract for FCR.

HSD should be a new page:

- route `/hsd`;
- files under `src/web/pages/hsd/`;
- a dedicated `hsd` postMessage block or a deliberate reuse of the current `rdr` block only if the
  data shape truly fits.

Do not make HSD a mode inside `rdr.js` unless the implementation proves the renderer is mostly
shared. The layouts already know how to host sibling pages, and separate page files keep the FCR
B-scope and HSD plan-view geometry from tangling.

## HSD data source

The first-pass source should be the existing faction datalink picture already used by RDR's
datalink-only contacts:

- local player aircraft and heading from the same snapshot values FCR already receives;
- player faction HQ from `aircraft.NetworkHQ`;
- aerial units from the shared known-position path, matching `BuildRdr`'s datalink pass;
- enemy aircraft only for v1;
- position from `TryGetKnownPosition`, corrected the same way existing telemetry handles floating
  origin;
- lock membership from the same selected-target set FCR and TGT use.

FCR and HSD should not disagree about what "locked" means. A contact selected on FCR, HSD, MAP, or
TGT is still one shared target-set membership.

### Relationship to current FCR/RDR data

Current RDR already contains both:

- `radar: true` contacts from the player's own radar;
- `radar: false` purple contacts from the datalink picture.

For this ticket, prefer making that separation explicit:

- FCR continues to show own-radar contacts. Whether it keeps purple datalink-only contacts is a
  design choice to settle before code; the cleaner split is FCR = own radar, HSD = datalink picture.
- HSD shows datalink-known aerial contacts around the aircraft, including contacts outside the
  FCR cone and beyond the selected FCR range.

If FCR keeps datalink-only contacts as purple bricks, the HSD page still adds value through 360-degree
geometry, not through a unique contact source.

## HSD visual design

Start as a basic DCS F-16 HSD-inspired plan view, not a full replica:

- black background using the existing MFD theme;
- ownship aircraft icon nose-up, centered in CEN mode — see "CEN/DEP display modes" below for DEP,
  which moves it;
- concentric dark-pink/magenta range circles centered on ownship, using shared theme token
  `--no-hsd-pink`;
- white text labels/readouts, reserving pink for HSD symbology instead of page text;
- no cardinal letters; they are easy to misread if the page evolves between nose-up and other
  orientation modes;
- closed teal FCR radar-coverage cone overlaid forward of ownship, clipped to the radar's own max
  range inside the currently selected HSD scale;
- datalink aerial contacts plotted in ownship-relative x/z space across 360 degrees;
- contact symbols use the same source colors as FCR: datalink-only purple, own-radar red, amber
  when locked (see "Focused lock vs. locked" below for the focused/other-lock split actually
  shipped); a stale datalink track (`HsdContact.Stale`, same 20m trust-radius check as
  `UnitInfo.Stale`, docs/tgt-stale-lock.md) goes white instead of its source color, since HSD has
  no other way to show "this position may no longer be accurate";
- optional simple velocity stubs once position plotting is proven.
- bottom-left readout mirrors FCR for the first locked contact: target name plus RNG/ALT/HDG in
  amber; the right footer stacks LINK count above LOCK count.

Keep the first version deliberately austere. The HSD's job is 360-degree awareness, so stable range
geometry and correct bearing are more important than decorative fidelity.

### Range scale

Use absolute display ranges rather than a fraction of radar max range. HSD is not bound to the
player radar's cone or maximum range. Shipped: the real DCS F-16 HSD ladders, independent per mode
(see "CEN/DEP display modes" below) — CEN 10/20/40/80/160 NM, DEP 15/30/60/120/240 NM. Metric labels
share the same underlying metre ranges and render via the player's unit system, same as FCR.

R+/R- step the one shared range index (see "CEN/DEP display modes" below for why it's shared, not
per-mode) and persist it in `sessionStorage` under `noxmfd.hsd.view`, separate from FCR's own
`noxmfd.rdr.view`.

## CEN/DEP display modes

DCS's F-16 HSD has two formats, toggled by its own physical control — shipped here as HSD's MODE
nav key (`hsd-mode` action, `hsd.js`'s `toggleMode()`):

- **CEN (Centered)** — ownship at the display's center, as described above. Grid rings at quarter
  fractions of the selected range (25/50/75/100%).
- **DEP (Depressed)** — ownship moved down near the bottom of the display, trading rearward picture
  for a much larger forward one on the same screen: the outer ring is still the full selected
  range, with inner rings at 1/3 and 2/3 of it (not quarters). This is also why DEP's range ladder
  reaches higher (240 NM max) than CEN's (160 NM max) — the extra forward screen space is what
  makes a bigger number still readable.

The range setting carries across a mode switch rather than each mode remembering its own: CEN 40NM
and DEP 60NM are treated as "the same" range, not two independent settings, because both ladders
are the same length and DEP[i] is exactly 1.5x CEN[i] at every step — the same ratio real DCS uses
to couple DEP's range to FCR's, coincidentally already built into the two ladders themselves. One
shared range index (`hsd.js`'s `rangeIdx`) into whichever ladder the current mode selects is enough
to get that translation for free, no separate coupling logic needed.

No FCR-range coupling itself, though (real DCS ties DEP's range to 1.5x whatever the FCR is set
to, not to HSD's own CEN setting) — HSD's range steps independently of FCR for now, per this doc's
own "Open questions" philosophy of not building a coupling nobody asked for yet.

Real DCS's exact ownship-offset and ring-radius pixels aren't published anywhere this doc could
verify against, so `hsd.js`'s `CEN_CY`/`CEN_OUTER`/`DEP_CY`/`DEP_OUTER` are a reasoned approximation
(marked `ponytail:` in the source) rather than a pixel-accurate replica: DEP pushes ownship to
roughly 83% of the way down the panel and sizes the outer ring to reach the same header clearance
CEN's does, leaving only a small sliver of range visible behind. Retune against real DCS reference
screenshots if pixel-accurate matching ever matters.

### Contact filtering

First pass:

- aerial enemy contacts only;
- show only contacts whose known position falls within the selected HSD range;
- no ground units, ships, buildings, route waypoints, or mission markers;
- no attempt to classify fighter/bomber/helo unless the existing unit definition makes it trivial.

Friendly datalink contacts are an explicit open question. They are useful on a real HSD, but this
repo's current target pages mostly focus on enemy/other-faction awareness. Do not add friendlies
without checking whether the game exposes them through the same faction-known path and whether the
user wants them visible.

## Interaction

FCR keeps its acquisition cursor and Select-to-lock behavior.

HSD carries the same cursor and reuses the identical target-set commands:

- Select on unlocked contact: `target.select`;
- Select on locked contact: `target.deselect`;
- hover/nearest-contact behavior follows FCR's existing hit-test pattern.

No HSD-specific lock state — `tg` on an item is the same target-set membership flag FCR and TGT
already read.

### Focused lock vs. locked (cycling-locked-targets follow-up)

The target set can already legitimately hold more than one `tg:1` contact at once (Select can lock
several in a row); the bottom readout has always described only the first one encountered, without
a real concept of "which lock is the one currently being read." Both pages now distinguish this
explicitly ahead of a planned cycling-locked-targets feature (a HOTAS action to step which lock is
focused, not built yet):

- **Focused lock** — the one the bottom readout currently describes. Icon amber, ring amber.
- **Any other simultaneous lock** — still part of the target set, but not what's being read out
  right now. Icon keeps its ordinary source color (red if own-radar, purple if datalink-only —
  same as an unlocked contact of that source), ring stays amber — the ring is "this is locked,"
  independent of which lock is focused or what detected it.

Until a real focus field exists, "focused" is simply the first locked contact each page's own
render loop encounters, matching the bottom readout's existing behavior exactly (`rdr.js`'s
`first`, `hsd.js`'s `firstLocked`) — swapping in a genuine focused-target id later needs no other
change here, since the color logic already only asks "is this contact the focused one," not "is
this contact literally first."

## Telemetry and protocol

Preferred first shape:

```json
{
  "present": true,
  "metric": false,
  "hdg": 123.4,
  "items": [
    { "id": 42, "x": 1000, "z": -5000, "alt": 4200, "hdg": 270, "tg": 0, "n": "F-16" }
  ]
}
```

The page can project contacts itself from ownship-relative position and ownship heading, matching
the existing browser-side geometry style used by FCR. If `BuildRdr` already has a helper that
computes `az`/`rng`, keep HSD separate anyway: HSD needs full 360-degree x/z projection, not cone
culling.

## Implementation steps

1. **Navigation and routes**
   - [x] Add `hsd` to `nav-model.js`, `layout-pages.js`, route coverage, and shell dispatch.
   - [x] Update `NAV.rdr` into the FCR/HSD sibling group with marks.
   - [x] Update split-slot placement and range decorators.

2. **FCR naming pass**
   - [x] Keep route/action `/rdr` and action `rdr`.
   - [x] Update visible labels from RDR to FCR where they name the sensor page, while preserving route
     names where they name existing infrastructure.
   - [x] Keep purple datalink-only bricks for this first pass so existing RDR behavior is preserved.

3. **Telemetry**
   - [x] Add an `hsd` block to `TelemetrySnapshot`/`TelemetryJson`.
   - [x] Build aerial datalink contacts from the player's faction-known picture.
   - [x] Include lock membership and unit names consistently with FCR/TGT.

4. **HSD page**
   - [x] Add `src/web/pages/hsd/hsd.html`, `.css`, `.js`, and a small projection test.
   - [x] Draw ownship, dark-pink rings, FCR coverage cone, source-colored contacts, and locked markers.
   - [x] Add HSD-specific range persistence and R+/R- actions.

5. **Shell forwarding**
   - [x] Mirror the latest `hsd` block in classic and F-35 shells.
   - [x] Forward it to full-view frames and split/portal panes.

6. **Preview and verification**
   - [x] Extend preview mocks/routes for `/hsd`.
   - [x] Run JS tests and `tools/ci-check.ps1`.
   - [x] Verify `/hsd` in `serve_web.py`; full visual layout has a Playwright render check.
   - [x] Verify in `serve_web.py` classic full, classic split, and F-35 layouts.

## Manual game checks

Confirmed in normal play since shipping.

## Open questions

- Should FCR drop datalink-only purple contacts after HSD has been live-tested, or keep them as a
  forward-scope cue?
- Should HSD show friendlies, enemies only, or eventually both with different symbols?
- What range presets feel right for Nuclear Option's map scale and aircraft speeds? (DEP/CEN ship
  with DCS's own ladders as a starting point — see "CEN/DEP display modes".)
- Should DEP's range eventually couple to FCR's (real DCS: DEP range = 1.5x FCR range), or stay
  independent as shipped?
- Should the MAIN entry continue to say RDR, or should the visible destination eventually become
  FCR with HSD reachable only from inside it?

## Related documents

- [RDR page](rdr-page.md)
- [TGT datalink cancel](tgt-datalink-cancel.md)
- [Layouts](layouts.md)
- [Page cursor](page-cursor.md)
