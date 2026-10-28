# Battle Damage

[Issue #110](https://github.com/roke77/NOXMFD/issues/110).

## Goal

A new Immersion option, **Battle damage** (KEY page, INPUT & DISPLAY group, default OFF): an MFD page goes
offline when the in-game aircraft system it shows is destroyed. RDR fails with the radar, TGP with
the targeting camera; with only the laser designator lost, TGP keeps its feed and its laser
readouts read LASER FAIL. Flight-critical and utility pages never fail. With the option OFF
nothing changes.

## What the game gives us

The game has no display-damage state (the cockpit `TacScreen` only dies with the aircraft). Each
system instead destroys its own component (`Object.Destroy(this)`) when it is lost:

| System | Component | Destroyed when |
|---|---|---|
| Radar | `Radar` (`Unit.radar`, a `TargetDetector`) | its part reaches 0 HP or detaches (`Radar.TargetDetector_OnApplyDamage` → `DisableTargetDetector`) |
| Targeting camera | `TargetCam` (`Aircraft.targetCam`) | its part or the cockpit part detaches (`TargetCam_OnDetach`) |
| Laser designator | `LaserDesignator` (`Aircraft.GetLaserDesignator()`) | its part detaches |

All three also die with the aircraft (`onDisableUnit`). A destroyed component compares `== null`
under Unity's overload, which is the whole liveness test.

`Radar.OnDestroy` calls `aircraft.SetRadar(null)`, which falls back to `defaultRadar` — so the field
can't be read statelessly to tell "lost" from "never fitted". Instead, **fitted latches** the first
time a system is seen alive on the current aircraft (reset on a new aircraft reference): alive →
`ok`, fitted but dead → `fail`, never seen → `none`. A component that comes back (repair, respawn)
reads `ok` again.

The HSD partial case needs no code: `BuildHsd` reads own-radar detections through
`player.radar != null`, and `BuildRdr` through `player.radar as Radar` + `== null`, so a destroyed
radar already drops HSD's own-radar contacts and FCR cone (datalink contacts, waypoints and map
stay) and turns RDR's data into `present:false`. That is game state and holds with the option off.

## Design

**Plugin**

- `Immersion/BattleDamage.cs` (Unity-free, unit-tested): the latch logic, `{on, radar, tgp, laser}`
  JSON in a volatile `StateJson` rebuilt only on a change, and `TgpFailed`/`LaserFailed` for the
  internal MFD.
- `TelemetryReader.RefreshBattleDamage`, on the fast tick after `PushSnapshot`: three Unity null
  checks. `is Radar` keeps a radar-less airframe's plain `TargetDetector` from counting as a radar.
- `SseHub`: a change-gated `sysdmg` event, same shape as `themes`/`wpt-options` — sent on connect
  and on change only, never per frame.
- `ImmersionConfig.BattleDamage` (persisted, hidden from F1), `keybind.set-battle-damage`, and
  `battleDamage` on `/keybinds-config`.
- `InternalMfdTgpPage`: `TGP FAIL` (red, flickering ~3 s at the 10 Hz refresh, then steady) and a
  `LASER FAIL` strip. The internal HSD follows the game like the web HSD; RWR never fails.

**Web**

- `telemetry-source.js` relays `sysdmg` up as `sysdmg-push`. The classic shell caches it
  (`RELAY_MESSAGES`, `SYSDMG_PAGES = ['rdr','tgp']`) and replays it on frame/pane load and on
  `showPage`; the F-35 lists it in `PAGE_FEEDS` for `rdr`/`tgp`, so portals replay it on mount.
- The server sends `sysdmg` only on connect and on change, and the map tap (an iframe declared
  before the shell's scripts) can receive the on-connect copy before the shell listens — found in
  game: a display opened after a crash never showed TGP FAIL. The tap keeps the last status
  (`rebroadcastSysdmg`), and each shell sends `sysdmg-request` once its listener is attached
  (answered by map.js and the F-35's telemetry-tap.js).
- `services/battle-damage.js`: the page → system rules (`failedSystem`, `laserFailed`) and
  `createFailScreen(doc, page)` — an opaque `.bd-fail` layer (theme.css) with the `<SYSTEM> FAIL`
  banner. It sits inside the page document, so it blanks content and swallows its clicks while
  the shell's bezel/OSB labels, outside the page, keep navigating.
- The flicker is a one-shot 3 s CSS animation (`both` fill, holds full opacity), so a settled
  failed page runs nothing. It plays only for a loss seen while the page is up; a page opened on
  an already-failed system settles at once.
- TGP: `#tgp-laser-fail` (visible in every resolution — Native's overlay is baked into the video)
  and the HQ overlay's `[LASE]` tag reads `[LASER FAIL]`. The TGP CFG view (`#cfg`) hides the fail
  screen, since it configures rather than shows the pod.

**Never fail**: ADI, HSI, HUD, SYS, MAIN and hubs, and every config/utility page — they have no
entry in `PAGE_SYSTEM`. Extension pages are out of scope.

## Testing

- `tools/tests/BattleDamageTests.cs`: not fitted vs failed, recovery, respawn, laser subsumed by a
  dead camera, option off, no aircraft.
- `services/battle-damage.test.js`: page → system mapping, never-fail pages, not fitted, the TGP
  laser partial, and the fail screen's flicker-once / settle-on-open behaviour.
- `pages/tgp/tgp.test.js`: LASER FAIL tag and `[LASER FAIL]` with the feed still running; TGP FAIL
  subsuming it; option off.
- Preview harness: `GET /__sysdmg?radar=fail&tgp=ok&laser=fail` sets the mocked status (`on`
  follows the KEY toggle). Verified in classic full view, classic split and F-35 portals.

### Live-game checks still needed

- Shoot off / zero the radar part: RDR flickers then shows RADAR FAIL; HSD drops own-radar
  contacts and the FCR cone, datalink contacts stay.
- Lose the TGP camera (its part or the cockpit detaching): TGP flickers then shows TGP FAIL, on
  the web page and the internal MFD pane.
- Lose only the laser designator: TGP feed keeps running with LASER FAIL (web and internal pane).
- Repair / respawn: every page recovers.
- An airframe without radar never shows RADAR FAIL; option OFF changes nothing.
- Aircraft destroyed: all three components die with it, so RDR/TGP go to FAIL — confirm that
  reads right next to the game's own dead cockpit.
- `TargetCam.Update` destroys itself if `aircraft.Player` is null or not local; confirm no false
  TGP FAIL at spawn.
