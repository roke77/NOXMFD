# RDR page

A new NAV item, **RDR**, showing the live air contacts the player's *own onboard radar*
is currently detecting. If the player's aircraft has no radar, the page shows a
`— not available —` placeholder instead.

Scope for this first pass: **air-only** contacts (aircraft/missiles that are `air`), sourced
**only** from the local aircraft's radar — not the faction datalink, not RWR, not visual/IR
sensors. This is deliberately narrower than TGT (which shows the whole shared target picture).

## Feasibility — explored, confirmed viable

The game already computes exactly this list for us; we don't reimplement radar detection.

### The data source

- `GameManager.GetLocalAircraft(out Aircraft ac)` → the player aircraft (already used all over
  `TelemetryReader.cs`, `CommandDispatcher.cs`, etc.).
- `ac.radar` (`Unit.radar`, a public `Radar`) → **null when the aircraft has no radar**. That
  single null check drives the placeholder. (Already used at `TelemetryReader.cs:160`.)
- `Radar : TargetDetector`, and `TargetDetector.detectedTargets` is a **`public List<Unit>`**,
  cleared and repopulated every scan by `RepeatSearch()` → `TargetSearch()` → `RadarCheck()`.
  So `ac.radar.detectedTargets` **is** the live "what my radar sees" list — no reflection, no
  private-field poking.

### Why this list is the right one

- `RepeatSearch()` runs client-side for the local aircraft — it's guarded by
  `attachedUnit.IsServer || GameManager.IsLocalAircraft(attachedUnit)`, so even as a non-host
  multiplayer client, the player's own radar runs its own search and fills `detectedTargets`.
  *(Caveat: confirm live in an actual MP client session; the guard says it should work, but we
  haven't run it.)*
- `RadarCheck()` already applies the real detection model: max range (`RadarParameters.maxRange`),
  radar cone FOV (`radarCone`), RCS / clutter / doppler / ECM via `RadarParams.GetSignalStrength`,
  and jamming (`IsJammed()`). Contacts that fall out of the cone, out of range, or get jammed
  simply aren't in the list. That's precisely "detectable by the onboard radar."
- Same-faction units are excluded at the source (`RadarCheck`/`VisualCheck` skip
  `item.NetworkHQ == attachedUnit.NetworkHQ`), so `detectedTargets` is already enemy-only.

### Air-only filter

Each entry is a `Unit`, so `unit.definition.typeIdentity.air > 0.5f` selects aircraft/air
contacts — the exact pattern already at `TelemetryReader.cs:857` (`ti.air > 0.5f`). Everything
needed to render a contact (position via `GlobalPosition()`, `speed`, `definition.unitName`,
faction) is `Unit` surface already consumed by MAP/TGT.

### One nuance to design around

`detectedTargets` is repopulated asynchronously: `RadarCheck()` calls
`DetectorManager.RequestRadarCheck(...)`, and the actual add happens in a deferred job callback
(`DetectTarget`). Between clear and refill the list can be momentarily empty/partial. The scan
also isn't every frame — it runs on `checkInterval` / `alertCheckInterval`. So the page should
read this on our normal telemetry tick and tolerate a list that updates a few times per second,
not per frame. (This matches how a real radar display sweeps, so it's a feature, not a problem.)

## Layout — locked (F-16 FCR B-scope)

Imitates the F-16 Fire Control Radar B-scope, drawn in the page's native green-on-black
(`--no-green` contacts, smoked-white grid, `--no-amber` for the locked target), same SVG-scope
approach as RWR but **rectangular** instead of polar.

- **Frame:** ownship implied at bottom-center (white caret). Horizontal axis = relative bearing
  off the nose; vertical axis = range, 0 at ownship → max at top.
- **Range scale:** fixed at the radar's own max range (`RadarParameters.maxRange`) — no selectable
  scale for v1.
- **Azimuth span:** clamp the horizontal to the **radar's actual cone half-angle** (`Radar.radarCone`),
  not a fixed ±60°, so contacts spread across the real FOV. Scan-limit lines mark the cone edges.
- **Contacts:** solid green bricks positioned by bearing × range, **aircraft only** (missiles
  filtered out). Each brick carries a short **velocity-vector stub** — a line off the brick in the
  direction of the target's horizontal motion (shows hot/cold/beaming aspect). *(Frame of the stub
  — north-up vs ownship-relative aspect — is an implementation detail; derive from the contact's
  velocity, resolve during step 2.)*
- **Locked/bugged target:** turns `--no-amber`, gains a surrounding **circle** (not a box), keeps
  its velocity stub. **Multiple targets can be locked at once** — each locked contact gets the
  amber brick + circle.
- **Data readout (bottom):** always the **first** target in the locked list — its `NAME` (kept —
  we have `definition.unitName`), plus `RNG`, `ALT`, `HDG`, and a `LOCK N` count of how many are
  locked.

### Locking reuses TGT's target set (decided)

RDR does **not** invent its own lock mechanism. "Locked contacts" **are** the existing TGT
selected-target set — the same list TGT shows and the same `target.select` / `target.deselect`
commands drive it. Consequences:

- Select on a contact = `target.select` for that unit; the amber brick+circle just reflects
  membership in that shared set.
- "First locked" = first entry in that existing selected-targets list (already mirrored to the
  shell as `targetsData`), so the readout has a well-defined source with no new state.
- Locks made on RDR show up on TGT and vice-versa — one target picture, two views. This is why
  RDR is aircraft-only at the scope level but leans on TGT's machinery underneath.
- **Corner block:** mode (`RWS`), range-scale number, page tag.
- **Placeholder:** `— not available —` when `ac.radar == null`.

Reference mockup: rendered in-conversation (F-16 FCR B-scope with two-bar acquisition cursor,
velocity stubs, one amber-locked contact).

### Interaction

- **PAD cursor** drives the F-16-style acquisition cursor: **two vertical bars** slewed by
  Cursor Up/Down/Left/Right (reuse `createPadCursor`, same as MAP/TGT/HUD).
- **PAD Cursor Select** locks the aircraft between the bars by adding it to TGT's selected-target
  set (`target.select`). Select can lock several contacts; the readout tracks the first entry in
  that set.

## What the page needs (not built yet — pending go-ahead)

Mirrors the existing frame-hosted pages (RWR is the closest analog — bezel radar contacts).

1. **Plugin (`TelemetryReader.cs` + `TelemetrySnapshot.cs` + `TelemetryServer.cs`):** on the
   telemetry tick, if `ac?.radar == null` emit a "no radar" flag; else project each air entry of
   `ac.radar.detectedTargets` into a small contact record (position relative to own-ship or
   bearing/range, speed, name, faction/hostility) and serialize as a new SSE payload.
2. **Frontend page `src/web/pages/rdr/`:** the B-scope SVG renderer (the locked layout above,
   made reactive), plus the `— not available —` placeholder state.
3. **Shell wiring (`src/web/shell/mfd.js`):** register `rdr: '/rdr'` in `FRAME_PAGES`, add its
   own RDR NAV slot, forward the contact stream to the frame like RWR's forwarder does.
4. **PAD cursor + lock:** wire `createPadCursor` for the two-bar acquisition cursor; on Select,
   drive TGT's existing `target.select` for the contact under the cursor (no new lock mechanism —
   see "Locking reuses TGT's target set" above).

## Resolved (was: open questions)

- **Layout:** F-16 FCR B-scope, its own visual identity, distinct from RWR's polar scope. ✔
- **Missiles:** excluded — aircraft only. ✔
- **PAD cursor:** interactible — cursor slews the two-bar acquisition cursor, Select locks. ✔
- **NAV slot:** RDR gets its own NAV slot (RWR = who paints me, TGT = shared picture, RDR = what
  my radar paints — three distinct things). ✔
- **Range scale:** fixed at radar max range. ✔  **Azimuth:** clamp to radar cone half-angle. ✔
  **Readout:** keep target name. ✔  **Bugged symbol:** amber box. ✔  **Velocity stub:** yes. ✔
