# RDR hub — FCR and HSD pages

## Status

In progress on branch `rdr-fcr-hsd` for [issue #60](https://github.com/roke77/NOXMFD/issues/60).

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
  { label: 'R+',   action: 'hsd-range-in' },
  { label: 'R-',   action: 'hsd-range-out' },
]
```

Keep action `rdr` for FCR so existing routes, shell dispatch, focused-cursor behavior, and saved
layouts continue to land on the inherited page. Add action `hsd` for the new page.

The R+/R- row remains a page-local range rocker. FCR keeps the current radar-range stepping. HSD
gets its own range stepping because its scale is a real circular map range, not a fraction of a
radar cone.

### Layout placement

- Classic full view: add `hsd` to `LayoutPages.CLASSIC_FULL`.
- Classic split view: add `hsd` to `LayoutPages.CLASSIC_SPLIT`.
- F-35 glass: add `hsd` to `LayoutPages.F35`.
- Classic split slots: add slots for the five `NAV.rdr`/`NAV.hsd` entries. Keep FCR/HSD adjacent.
- F-35 edge grid: keep the FCR/HSD mark lights from the `NAV` table; add a RANGE decorator between
  the HSD R+/R- entries if the labels stay adjacent there.
- PAD cursor focus: include `hsd` only if the first implementation makes contact selection or cursor
  panning active. If HSD starts read-only, leave it out of `PAD_CURSOR_PAGES`.

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
- ownship aircraft icon at the center, nose-up;
- concentric dark-pink/magenta range circles centered on ownship, using shared theme token
  `--no-hsd-pink`;
- white text labels/readouts, reserving pink for HSD symbology instead of page text;
- no cardinal letters; they are easy to misread if the page evolves between nose-up and other
  orientation modes;
- closed teal FCR radar-coverage cone overlaid forward of ownship, clipped to the radar's own max
  range inside the currently selected HSD scale;
- datalink aerial contacts plotted in ownship-relative x/z space across 360 degrees;
- contact symbols in HSD pink by default, amber when locked;
- optional simple velocity stubs once position plotting is proven.

Keep the first version deliberately austere. The HSD's job is 360-degree awareness, so stable range
geometry and correct bearing are more important than decorative fidelity.

### Range scale

Use absolute display ranges rather than a fraction of radar max range. HSD is not bound to the
player radar's cone or maximum range. Initial candidates:

- Imperial: 10, 20, 40, 80 nautical miles;
- Metric: nearest clean kilometre equivalents, or share the same underlying metre ranges and render
  labels in the player's unit system.

R+/R- should step this HSD-specific range and persist it in `sessionStorage` under a separate key
from FCR's current `noxmfd.rdr.view`.

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

HSD v1 can be read-only. If cursor selection is added in the first implementation, reuse the same
target-set commands:

- Select on unlocked contact: `target.select`;
- Select on locked contact: `target.deselect`;
- hover/nearest-contact behavior follows FCR's existing hit-test pattern.

Do not add HSD-specific lock state.

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
   - [x] Draw ownship, dark-pink rings, FCR coverage cone, contacts, and locked markers.
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

These need a live mission:

- FCR still renders own-radar contacts, locks, range stepping, and pitbull markers.
- FCR/HSD marks switch correctly from the physical MFD controls.
- HSD shows datalink-known aircraft outside the FCR cone.
- HSD shows contacts beyond the own radar's current displayed range when datalink provides them.
- HSD range stepping is independent from FCR range stepping.
- Selection, if implemented, locks the same target set TGT and FCR display.
- Aircraft without radar still reach HSD if datalink data exists; FCR can remain unavailable.

## Open questions

- Should FCR drop datalink-only purple contacts after HSD has been live-tested, or keep them as a
  forward-scope cue?
- Should HSD show friendlies, enemies only, or eventually both with different symbols?
- Should HSD become selectable after the projection and datalink source are proven?
- What range presets feel right for Nuclear Option's map scale and aircraft speeds?
- Should the MAIN entry continue to say RDR, or should the visible destination eventually become
  FCR with HSD reachable only from inside it?

## Related documents

- [RDR page](rdr-page.md)
- [TGT datalink cancel](tgt-datalink-cancel.md)
- [Layouts](layouts.md)
- [Page cursor](page-cursor.md)
