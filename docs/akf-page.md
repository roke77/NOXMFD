# AKF — advanced kill feed MDT page

## Status

**Implemented, not yet in-game verified** (branch `advanced-kill-feed`,
ticket [#34](https://github.com/roke77/NOXMFD/issues/34)). Every piece
described below — the two Harmony patches, `AkfTracker`, the wire format,
the page itself, and the HUD FEED toggle — is built and browser-verified
(`tools/serve_web.py`, synthetic `'akf'` messages) against `dotnet build -c
Release`. Confirming the Harmony hooks actually fire on a real kill, and the
FEED toggle actually hides the native ticker, needs a live mission.

## What this replicates

Four pieces, scoped this way after design review with the ticket author:

- **ALL kill feed** — a live replica of the game's own HUD kill-feed ticker
  (`MessageUI.killFeedText`/`MessageManager.RpcKillMessage`): every kill,
  friendly/hostile colored, growing toward the bottom of its panel.
- **PLAYER kill feed** — the same event stream, filtered to kills credited
  to the local player, with the player's own aircraft name omitted (it's
  always the same aircraft, so naming it on every line is redundant) and a
  weapon name appended where resolvable.
- **SESSION KILLS** — a per-session tally of the player's own kills, broken
  down by type (aircraft/vehicle/ship/building — the same split BDF/PAL
  already use).
- **FUNDS** and **ENEMY VALUE LOST** — funds gained/spent this session and
  the summed value of enemy units the player has personally destroyed.

Every stat on the page other than the ALL feed and FUNDS is scoped to the
player's own kills, not the whole faction — a deliberate choice over reading
`FactionHQ.missionStatsTracker` (faction-wide, and would need no new
tracking) so every number on the page answers "what did *I* do this
session," consistently.

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
- PLAYER feed / SESSION KILLS / ENEMY VALUE LOST: the same records, filtered
  to kills where `killerID` resolves to the local player's own unit. Tallied
  by `killedType` (`KillType`: Aircraft/Vehicle/Building/Ship — Missile
  intercepts excluded from the type grid, matching BDF/PAL's own split) and
  summed via `killedUnit.definition.value` — the same per-unit value field
  `FactionHQ.ReportKillAction`'s reward formula and
  `MessageManager.KillFeedFilter`'s minimum-value gate already read, which
  resolves the ticket's open question about a per-unit cost figure.

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

`ponytail:` this is a last-fired-weapon-**by-this-attacker** heuristic, not
per-victim tracking — firing two different missile types within the TTL
window before either scores a kill can attribute the wrong one. Upgrade
path is per-victim tracking inside `BlastFrag`'s own collider loop, if this
proves inaccurate in practice.

Gun/cannon kills go through `DamageEffects.ArmorPenetrate`, which carries
no weapon-identity parameter at all — attributing those needs a separate
hook further up the firing path (likely `WeaponMount`/`Cannon`), not yet
investigated. Gun kills ship without a weapon suffix until that lands,
reading like the feed's existing no-killer lines (e.g. "T-90 was
destroyed").

### Funds gained/spent

Polled, no Harmony: `TelemetryReader.ScanWorld` (1 Hz, the same cadence as
`BuildBdf`/`BuildMis`) reads the local player's own `FactionHQ.factionFunds`
(`GameManager.GetLocalHQ`, not a fixed BOSCALI/PRIMEVA identity the way
BDF/PAL read theirs) each tick, diffs it against the previous tick, and
accumulates the positive delta into GAINED and the negative delta into
SPENT on `AkfTracker`.

## Wire format and page

`TelemetryServer.cs` gets a new `AkfBlock`, alongside `MisBlock`/`ObjBlock`,
appended to the main snapshot the same way: `{"all":[...], "player":[...],
"kills":{"aircraft":n,"ship":n,"vehicle":n,"building":n}, "value":n,
"fundsGained":n, "fundsSpent":n}`. Each feed entry: `{"a":"attacker name or
null","h":friendly/hostile,"v":"victim name","vh":friendly/hostile,
"verb":"...","w":"weapon or null"}`.

The page (`src/web/pages/akf/`) renders two side-by-side feed columns — ALL
(left, wider) and PLAYER (right) — each growing downward with the newest
line at the bottom (`flex-direction: column`, `justify-content: flex-end`,
scrolled to bottom on update). Friendly names read in `var(--no-blue)`,
hostile in `var(--no-red)`, the verb and "with" in `var(--no-white-dim)`,
the weapon name in `var(--no-green-dim)`. Three stat cards sit below the
feeds: SESSION KILLS (type grid), FUNDS (gained/spent/net), and ENEMY VALUE
LOST.

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
