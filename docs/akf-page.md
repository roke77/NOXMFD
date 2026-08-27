# AKF — advanced kill feed MD page

## Status

**Implemented on `main` and in-game verified** (original branch `advanced-kill-feed`, ticket
[#34](https://github.com/roke77/NOXMFD/issues/34)). Confirmed across several
live sessions: the kill-message and weapon-attribution Harmony patches fire
correctly on real kills (including the AGM-48 salvo/gun-kill investigation
below), RANK/FUNDS track a live `FactionHQ`/`Player`, and a session's log
shows zero kill-message drops. The HUD FEED toggle and the PLAYER feed's
"incoming" lines (player killed / own ordnance intercepted) are built and
browser-verified (`tools/serve_web.py`) but not yet explicitly exercised
in-game.

## What this replicates

Four pieces, scoped this way after design review with the ticket author:

- **ALL kill feed** — a live replica of the game's own HUD kill-feed ticker
  (`MessageUI.killFeedText`/`MessageManager.RpcKillMessage`): every kill,
  friendly/hostile colored, growing toward the bottom of its panel.
- **PLAYER kill feed** — the same event stream, filtered to kills credited
  to the local player, with the player's own aircraft name omitted (it's
  always the same aircraft, so naming it on every line is redundant) and a
  weapon name appended where resolvable. Also carries **incoming**
  interactions: the player's own aircraft being destroyed, and any munition
  the player fired getting intercepted before reaching its target — both
  rendered in full (attacker shown, since it's not the player this time)
  with a left-accent marker distinguishing them from the player's own
  scored kills.
- **SESSION KILLS** — a per-session tally of the player's own kills, broken
  down by type (aircraft/vehicle/ship/building — the same split BDF/PAL
  already use).
- **FUNDS** and **RANK** — funds gained/spent this session, and the
  player's current `Player.PlayerRank` (the same integer the game's own
  `KillDisplay` flashes as "RANK n" on rank-up). Unlike every other stat on
  this page, RANK isn't session-scoped — it's the player's persistent rank,
  just surfaced here since this is otherwise the "how am I doing" page.

Every session-scoped stat on the page other than the ALL feed and FUNDS is
scoped to the player's own kills, not the whole faction — a deliberate
choice over reading `FactionHQ.missionStatsTracker` (faction-wide, and
would need no new tracking) so every number on the page answers "what did
*I* do this session," consistently.

## Data model

Two independent capture mechanisms, both mission-scoped — created alongside
`TelemetryReader`/`HudDeclutter` in `MissionLifecycle.StartReader`, torn
down with them in `StopReader`, which is already the session-reset boundary
this feature needs (no separate cleanup).

### Kill events

`MessageManager.RpcKillMessage(killerID, killedID, killedType)` is the
`ClientRpc` that drives the game's own kill-feed ticker, fired on every
kill. The public wrapper only runs on the sending/host side; a remote
client receiving the RPC never calls it — the actual per-observer execution
goes through Mirage's generated `UserCode_RpcKillMessage_...` method, which
is the correct Harmony patch target for a hook that must fire everywhere.
The generated method's numeric suffix is a weaver-generated hash tied to
the RPC's signature, not stable across a game update by name — the patch
resolves it via `TargetMethod()` searching `typeof(MessageManager)` for a
method whose name starts with `UserCode_RpcKillMessage`, rather than
hardcoding the current suffix.

The postfix resolves both `PersistentUnit`s (`UnitRegistry.TryGetPersistentUnit`),
the local player/HQ (`GameManager.GetLocalHQ`/`IsLocalPlayer`), and records
each kill into a new mission-scoped `AkfTracker`:

- ALL feed: every kill, in a capped ring buffer.
- PLAYER feed / SESSION KILLS: the same records, filtered to kills where
  `killerID` resolves to the local player's own unit. Tallied by
  `killedType` (`KillType`: Aircraft/Vehicle/Building/Ship — Missile
  intercepts excluded from the type grid, matching BDF/PAL's own split).
- PLAYER feed also carries **incoming** interactions — the player's own
  unit being killed, or a munition the player fired being intercepted —
  which are never tallied into SESSION KILLS (see `AkfKillEntry.PlayerIsVictim`
  in `AkfTracker.RecordKill`).

Each record's display text reuses `KillTypeExtensions.GetVerb(hasKiller)`
directly (`"shot down"`/`"destroyed"`/`"demolished"`/`"sank"`/`"was
destroyed"`), matching the game's own wording exactly.

### Weapon attribution (best-effort)

Kill-credit attribution (`Unit.ReportKilled`'s `damageCredit`) is keyed by
the attacking **unit**, not the weapon — a missile's damage is credited to
its **launching aircraft's** ID (`Missile.ownerID`, threaded into
`DamageEffects.BlastFrag`'s `dealerID` parameter), not the missile's own
ID. That's why the game's own feed already reads "F-16 shot down MiG-29"
and not "AIM-9X shot down MiG-29" — but it also means the munition identity
is gone by the time a kill fires.

`DamageEffects.BlastFrag(blastYield, blastPosition, dealerID, missileID)`
receives both IDs as parameters; `missileID` is accepted but unused in the
method body. A Harmony prefix on `BlastFrag` resolves `missileID`'s
`PersistentUnit.definition.unitName` and records it against `dealerID` in a
short-TTL map on `AkfTracker`. The kill-event postfix looks this up by
`killerID` to attach a weapon name to PLAYER-feed lines.

**A `BlastFrag`-only hook isn't early enough.** In-game testing found the
first kill of an AGM-48 salvo consistently missing its weapon name while
later kills had it — root cause, confirmed against the decompiled source: a
pierce-fuze warhead (`impactFuseDelay == 0`, the AGM-48's kind) kills
through `Missile.PenetrateObject` → `DamageEffects.ArmorPenetrate` →
`TakeDamage`, synchronously, and never calls `BlastFrag` for that kill at
all. A proximity/blast-fuze warhead does call `BlastFrag`, but only after
`Missile.ExplosionForceOnPhysicsFrame` awaits
`UniTask.WaitForFixedUpdate()` — a full physics tick later than the
missile's own `Detonate()`/kill. In a salvo this meant every kill was
quoting the *previous* missile's weapon (which only looked right because a
salvo repeats the same weapon type), and the first kill had nothing to
borrow.

Fixed with two more Harmony prefixes at the missile's own terminal-sequence
entry points, both synchronous and both running before any kill they can
cause: `Missile.PenetrateObject` (the pierce-fuze path) and
`Missile.Detonate(Vector3, bool, bool)` (the proximity/blast-fuze path,
recording weapon identity immediately instead of waiting on the deferred
`BlastFrag`). Both call the same
`AkfTracker.RecordWeaponHit(__instance.ownerID, __instance.persistentID)` —
`Missile` already carries its own `persistentID` and its launching
aircraft's `ownerID` as public fields, so no parameter injection from the
patched method is needed. The original `BlastFrag` prefix stays too, as a
fallback for anything that reaches it without going through either method
first.

`ponytail:` still a last-fired-weapon-**by-this-attacker** heuristic, not
per-victim tracking — firing two different missile types within the TTL
window before either scores a kill can attribute the wrong one. Upgrade
path is per-victim tracking, if this proves inaccurate in practice.

Gun/cannon kills go through `DamageEffects.ArmorPenetrate`, which carries
no weapon-identity parameter at all — attributing those needs a separate
hook further up the firing path (likely `WeaponMount`/`Cannon`), not yet
investigated. Gun kills ship without a weapon suffix until that lands,
reading like the feed's existing no-killer lines (e.g. "T-90 was
destroyed").

Confirmed in-game via a temporary diagnostic build: cannon rounds do call
`ArmorPenetrate` with a small nonzero `blastDamage` (observed ~0.04), which
forwards into `BlastFrag(blastDamage, position, dealerID,
PersistentID.None)` — but always with `missileID = PersistentID.None`,
which can never resolve to a unit/name. So this path was already
structurally incapable of naming a weapon, exactly as expected above; no
code change from this, just confirmation the gap is real and not a bug in
the existing hook.

### Funds gained/spent

Polled, no Harmony: `TelemetryReader.ScanWorld` (1 Hz, the same cadence as
`BuildBdf`/`BuildMis`) reads `FactionHQ.factionFunds` (`GameManager.
GetLocalHQ`, so it follows the player's own faction rather than a fixed
BOSCALI/PRIMEVA identity like BDF/PAL) each tick, diffs it against the
previous tick, and accumulates the positive delta into GAINED and the
negative delta into SPENT on `AkfTracker`.

`ponytail:` `factionFunds` is the **whole faction's** balance, not a
per-player figure — Nuclear Option has no such thing to read instead. In
solo play this tracks 1:1 with the player's own actions; in multiplayer any
teammate's purchase or AI-earned kill reward shows up here too, misattributed
as "the player's own" gained/spent. Confirmed against a real session's log
(GAINED-only deltas sized like kill rewards, `_fundsGained` correctly
resetting to 0 on a mission restart) but not distinguishable from a
teammate's activity by the log alone. Known ceiling, accepted.

## Wire format and page

`TelemetryServer.cs` gets a new `AkfBlock`, alongside `MisBlock`/`ObjBlock`,
appended to the main snapshot the same way: `{"all":[...], "player":[...],
"kills":{"aircraft":n,"ship":n,"vehicle":n,"building":n}, "rank":n,
"fundsGained":n, "fundsSpent":n}`. Each feed entry: `{"a":"attacker name or
null","h":friendly/hostile,"v":"victim name","vh":friendly/hostile,
"verb":"...","w":"weapon or null","pv":true (PLAYER feed only, omitted
otherwise) — marks an "incoming" line where the player is the victim, not
the (shown) attacker}`.

The page (`src/web/pages/akf/`) renders two side-by-side feed columns — ALL
(left, wider) and PLAYER (right) — each growing downward with the newest
line at the bottom (`flex-direction: column`, `justify-content: flex-end`,
scrolled to bottom on update). Friendly names read in `var(--no-blue)`,
hostile in `var(--no-red)`, the verb and "with" in `var(--no-white-dim)`,
the weapon name in `var(--no-green-dim)`. Three stat cards sit below the
feeds: SESSION KILLS (type grid), FUNDS (gained/spent/net), and RANK.

`mfd.js`/`f35.js` forward the new `'akf'` slice the same way `mis`/`obj`
are forwarded to their pages — the nav-wiring pass gave AKF its page slot
and default-landing behavior, not a live data feed.

## HUD FEED toggle

A fourth `HudDeclutterConfig` flag, the same shape as the existing three
(`HideWeaponAmmo`/`HideMinimap`/`HideTopBoxes`): `HideKillFeed`, toggled by
a new `FEED` button on the HUD page's declutter strip (alongside WEAPONS/
MINIMAP/FLIGHT), applied by `HudDeclutter` disabling `MessageUI`'s private
`killFeedText` `Graphic` component each ~0.5s interval, the same reflect-
once-then-toggle idiom already used for the boxed heading/altitude
readouts. Only `killFeedText` — `MessageUI`'s general message feed (join/
disconnect/HQ messages) is untouched.

## Out of scope this pass

- Gun/cannon weapon attribution (needs its own investigation into the
  firing path).
- A settings UI for the kill-feed ring-buffer size or the weapon-attribution
  TTL — fixed constants, no config.
- Cross-mission/session persistence — resets on mission end, same as every
  other session-scoped stat this mod tracks.
- A true per-player FUNDS figure — the game only tracks funds per faction;
  see the "Funds gained/spent" section above.
