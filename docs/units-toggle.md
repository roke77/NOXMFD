# Toggle Units keybind

[Issue #84](https://github.com/roke77/NOXMFD/issues/84) — a keybind that flips the game's own
Metric/Imperial preference, and the audit that followed to make every NOXMFD page actually follow
it.

## The keybind

`Keybinds.ToggleUnits()` flips `PlayerSettings.unitSystem` — the same public static field the
native Gameplay options menu's Unit System dropdown sets — between `Metric` and `Imperial`, and
also writes `PlayerPrefs.SetInt("UnitSystem", ...)` + `Save()`. That second step matters: the menu
itself doesn't set the field directly, only `PlayerSettings.LoadPrefs()` does (called from
`GameplayMenu.OnDestroy`), so skipping the `PlayerPrefs` write would let opening and closing that
menu silently revert the toggle back to whatever was last saved. `tgp.zoom.step`/`combat-mode.set`
already follow this "one static helper, called from both the keybind and `CommandDispatcher`'s
remote-keybind twin" shape — `units.toggle` does too.

## Why a top-level `metric` telemetry field

Every native readout (`UnitConverter.AltitudeReading`/`DistanceReading`/`SpeedReading`/etc.) reads
`PlayerSettings.unitSystem` live, so flipping it takes effect everywhere in the game immediately.
NOXMFD's own pages needed the same live signal, but the existing `RdrMetric` snapshot field (read
by `RdrBlock`/`HsdBlock`) is scoped inside those two blocks — a page like OBJ that has neither a
radar nor an HSD feed has no way to read it. `TelemetryJson.AppendFrameHeader` now also appends a
top-level `"metric"` field, the same "one value several unrelated pages need" reasoning already
used for `focusedTargetId`. `RdrBlock`/`HsdBlock` keep their own nested copies unchanged.

`telemetry-source.js` forwards this flag on the `'obj'`, `'mapinfo'`, and `'targets'`
postMessages; `mfd.js`'s `tgtTargetsMsg()` (which cherry-picks fields rather than forwarding a
message verbatim) carries it through to TD's split-pane mirror. F-35's `FEED_AS` rename forwards
the whole message object, so it needed no change.

## Per-page fixes

Each of these pages formatted a distance/altitude/speed value itself instead of asking the plugin
— confirmed against decompiled game source, then fixed to match:

- **TGP's HQ overlay** (`tgp.js`'s `applyOverlay`/`applyManualOverlay`) hardcoded RNG to km and
  ALT/SPD/REL to raw meters/mps with no unit suffix at all — a change already flagged in its own
  comment as a simplification. `TgpOverlay`'s `RangeM`/`AltitudeM`/`RelAltitudeM`/`SpeedMps`/
  `RelSpeedMps` floats stay as-is (`TgpFullScreen`/`TgpNativeOverlay` read them directly and format
  themselves already), but `TelemetryReader`'s snapshot copies are now pre-formatted strings
  (`TgpRangeReading`/`TgpAltReading`/`TgpRelAltReading`/`TgpSpeedReading`/`TgpRelSpeedReading`),
  the same "ready string, not a raw number" shape `TgpClosureReading` already used. The client just
  renders them verbatim.
- **OBJ**'s per-objective distance (`obj.js`'s `fmtRange`) and **WPT**'s DIST readout (`wpt.js`'s
  new `fmtDist`) both hardcoded km — these stay client-formatted (not pre-formatted server-side)
  because both are live, per-frame values `telemetry-source.js` derives from world x/z at the base
  frame's own rate rather than the plugin's 1 Hz refresh; each now checks its own `metric` flag and
  picks km or nm.
- **TGT** and **TD**'s target-list range column (`fmtRng`) were byte-identical copies of each
  other, both hardcoded to km — confirmed against the native `TargetListSelector_UnitItem`, which
  does call `UnitConverter.DistanceReading`. Consolidated into one shared
  `src/web/services/range-format.js` (`fmtRng(r, metric)`), imported by both pages instead of
  duplicated.

## TD's redraw gate needed its own metric check

Carrying `metric` on the wire wasn't enough for TD: unlike TGT, TD deliberately does NOT redraw on
every `'tgt-targets'` message — its leader table only repaints on a real select/deselect (an id-set
change, `idsKey()`), and its member list only repaints on a `'td-state-push'`/`'sqd-state'` push,
neither of which the toggle sends. `liveTargetsMetric` was updated on every message, but nothing
told either view a redraw was actually due, so an open TD page kept stale values until an unrelated
event happened to repaint it. `td-redraw-gate.js` (a pure sibling module, `src/web/README.md`'s
established pattern — split out because `td.js` itself can't be `import()`ed directly in a plain
Node test, its `/assets/...` specifiers only resolve through the real asset server) now decides,
given the last-applied id key/metric and the new message: the leader redraws on an id-set OR metric
change; the member view's redraw is metric-only, since `renderMember` already rebuilds wholesale on
every state push regardless of ids.

## Deliberately left as page-local, not shared further

`obj.js`'s `fmtRange` and `wpt.js`'s `fmtDist` are not folded into `range-format.js` alongside
TGT/TD's `fmtRng` — each has its own precision/spacing convention (OBJ drops the decimal above
10 km/nm; WPT always shows one; TGT/TD use a European decimal comma), so a shared function would
need to grow parameters for all three shapes rather than staying a plain `(value, metric)` call.
HSD's `rangeLabel`/`rangeUnits` and FCR's `rangeUnits`/`altUnits` (`docs/rdr-fcr-hsd.md`) follow
the same pattern independently, predating this change.
