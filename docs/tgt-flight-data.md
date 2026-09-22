# TGT flight-data columns — [issue #88](https://github.com/roke77/NOXMFD/issues/88)

**Status:** built, not yet in-game tested. `dotnet build -c Release` (0 errors) and the full
`*.test.js` suite exercise the field derivation and rendering; the underlying `HasDetail`/
`SpeedReading`/`AltReading`/`Heading` game data itself is only exercisable in a live mission.

## What it is

A DETAILED/COMPACT toggle at the bottom right of [TGT](../man/tgt.md)'s target-list footer —
same real-sliding-switch component as [AKF's own density toggle](akf-page.md), but the opposite
default (TGT starts COMPACT). DETAILED appends three columns after RNG: **SPD**, **ALT**, **HDG**.
(GRID joined this DETAILED-only group later, in the TYPE column follow-on —
`docs/tgt-target-type.md` — so DETAILED now shows four extra columns, not three; the
`HasDetail`/`SpeedReading`/`AltReading`/`Heading` mechanics below are unaffected by that move.)

No plugin-side change: `HasDetail`/`SpeedReading`/`AltReading`/`Heading` are the same per-unit
fields the map's own hover tooltip already reads off a contact (`map.js`). This feature is purely
carrying them one hop further — through `telemetry-source.js`'s target derivation and into TGT's
own row renderer — not deriving anything new.

## Design decisions

- **Client-local, not persisted.** Same as AKF's density toggle: nothing round-trips to the shell
  or the plugin, and the setting reverts to COMPACT on reload.
- **Opposite default from AKF.** AKF starts DETAILED; TGT starts COMPACT, since the flight-data
  columns are the less commonly needed view here (SPD/ALT/HDG matter far less for a filter/gate
  page than for AKF's feed).
- **`—` gated on `hd` (`HasDetail`), not on presence of a value.** A target that isn't an
  aircraft/missile, or whose lock has gone stale, has no `HasDetail` and always renders `—` in all
  three columns — the identical gate/placeholder the map's own tooltip already uses for this data.
- **`hd`/`sp`/`al`/`h` always come out defined**, never `undefined`, off `telemetry-source.js`'s
  derivation: `hd:false`, `sp:''`, `al:''`, `h:null` for a contact with no detail data, so the
  renderer never has to special-case a missing key.
- **The toggle component itself was ported wholesale from AKF's identical one, not extracted into
  a shared module.** The CSS is otherwise byte-identical modulo the `akf-`/`tgt-` class prefix; the
  two pages' JS differs in what the toggle drives (AKF re-renders its feed, TGT flips a CSS class
  on the panel), so sharing only the CSS half didn't seem worth a new shared file for two
  instances. Revisit if a third page needs the same toggle.

## What is built

| File | What |
|---|---|
| [`src/web/pages/tgt/tgt.js`](../src/web/pages/tgt/tgt.js) | The DETAILED/COMPACT toggle (`compact`, default `true`) and its click handler; `fmtHdg()` (same normalization as `map.js`'s tooltip); `renderTargets()` fills SPD/ALT/HDG with `—` placeholders per the `hd` gate. |
| [`src/web/pages/tgt/tgt.html`](../src/web/pages/tgt/tgt.html) | The toggle markup (ported from `akf.html`) and the three new header cells. |
| [`src/web/pages/tgt/tgt.css`](../src/web/pages/tgt/tgt.css) | `.has-flight-col` show/hide (mirrors `.has-td-col`'s mechanics), the toggle's styling (ported from `akf.css`), and the four grid-template-columns combinations for has-td-col × has-flight-col. |
| [`src/web/services/telemetry-source.js`](../src/web/services/telemetry-source.js) | Target derivation carries `hd`/`sp`/`al`/`h` through from each contact, with safe defaults for a contact with no detail data. |
| [`src/web/services/telemetry-source.test.js`](../src/web/services/telemetry-source.test.js) | Covers both a `HasDetail` contact (values pass through unchanged) and one without (defaults to `hd:false`/`sp:''`/`al:''`/`h:null`, never `undefined`). |
| [`tools/preview-mock.js`](../tools/preview-mock.js) | Two mock targets (one airborne, one grounded aircraft) carry `hd`/`sp`/`al`/`h` so DETAILED mode has real data to render in the harness. |
| [`man/tgt.md`](../man/tgt.md) | Documents the toggle and the three columns. |

## Verification performed

- `dotnet build -c Release` — 0 errors.
- Full `*.test.js` suite, including the new `telemetry-source.test.js` cases above.
- Not yet checked in the `serve_web` harness or in-game.
