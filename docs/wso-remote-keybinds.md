# WSO — remote keybind listening from a second browser (planning)

## Status

**Planning.** Not started. Requested by a player who flies with a second person acting as WSO
(weapons systems officer): the pilot flies, the WSO connects to NOXMFD from a separate device over
the LAN and works MAP/TGT/weapon-selection. The WSO currently has no way to trigger those actions
with a keypress the way the pilot does at the keyboard/HOTAS — every WSO action has to be a mouse
click on the web page.

## Goal

Let a browser optionally listen for keydown/keyup events and translate them into the same
`/command` POSTs NOXMFD's own bind registry already understands — so a WSO's browser can use the
same key layout the pilot configured on the KEY page, without the WSO needing physical access to
the game PC's keyboard or HOTAS.

**Opt-in, off by default, one browser tab at a time in practice.** This is the important framing
for the whole feature: it isn't "give every connected browser a keyboard," it's "let one
specifically-designated browser act as an input source." Turning it on in the wrong place (see
"Same-PC redundancy" and "Multiple senders" below) creates real, confusing double-input bugs, not
just visual clutter — so the toggle's UX has to make the risk legible, not just technically gate
it.

## What already exists (recap — full detail in the investigation this doc is based on)

- **`/keybinds-config`** already exposes NOXMFD's own bind registry as JSON: `{binds: [{id,
  section, label, description, key, joyButton, joyNum, ...}], ...}` — `key` is a Unity `KeyCode`
  name, `id` is the stable action id (`"flares"`, `"cycle-guns"`, `"tgt-next"`, ...). This is the
  same registry the KEY page renders and the same data `src/web/shell/layout-keybinds.js` already
  fetches to match a raw browser `KeyboardEvent` against one configured key (for SAVE/LOAD LAYOUT,
  a client-side-only action). That module is the direct precedent for this feature's steps (a) and
  (b) — fetch the table, match keydown against it — just not yet paired with a `sendCommand` call.
- **Most WSO-relevant actions are already invokable via `/command` today**: `target.select`,
  `target.deselect`, `weapon.select`, `master-arms.set`, `combat-mode.set`, `avn.toggle` (radar,
  gear, engine, guns-linked, turret), the full `tgt.*` filter panel, `soi.next`/`soi.prev`. No new
  game-side plumbing needed for these — a keydown handler can call `sendCommand` today.
- **A handful of actions need one new `CommandDispatcher` entry each**, calling an existing
  (currently `private`) method the keybind `Poll()` path already uses — cycle guns/missiles/bombs
  (`WeaponSelectors.CycleGun/CycleMissile/CycleBomb`), flares/jammer
  (`Keybinds.Drive`), dedicated gear up/down (`Keybinds.DriveGear`), MAP/TGT highlight nav
  (`TelemetryServer.MapAction`), SOI nav up/down/select (`TelemetryServer.SoiAction`). No Harmony,
  no new game reverse-engineering — see "Gap analysis" below for the full list.
- **Gun Trigger / Weapon Release / Jammer Pod fire are the one genuinely different case**:
  `WeaponSelectors.FireGun/FireRelease/FireJammerPod` are written assuming a continuous per-frame
  caller (`_gunFrame`/`_relFrame` hold-vs-tap detection) — a discrete HTTP POST doesn't naturally
  fit that shape. See "Fire actions" below.
- **The command channel has no auth, no per-sender identity beyond an optional `cid`, and is
  explicitly designed to be reachable from any device on the LAN** (`TelemetryServer.Start()`
  binds all interfaces on purpose, with auto-elevated firewall rules, precisely so a tablet/second
  PC can reach it). Adding a second command-sending browser doesn't cross a trust boundary this
  design doesn't already accept — the existing model is "anyone who can reach this LAN port can
  act," unchanged by this feature.
- **`/soi-instances`** already gives a live list of connected browsers: `{instances: [{conn, cid,
  remote, upSec}]}`, keyed by server-observed connection, including each client's remote address.
  This is the seam the same-PC detection below reuses — nothing new has to be built to *see* who's
  connected, only to *use* that for a warning.

## Gap analysis: command-channel coverage for WSO-relevant actions

| Action | `/command` today? | Work needed |
|---|---|---|
| Target select / deselect | Yes | none |
| Weapon select (by name) | Yes | none |
| Master Arms on/off | Yes | none |
| Combat mode A/A · A/G · ALL | Yes | none |
| Radar / engine / gear / guns-linked / turret toggle | Yes (`avn.toggle`) | none |
| TGT filter set/only/reset/clear/datalink/stale/laser/hud | Yes (`tgt.*`) | none |
| SOI next/prev | Yes (`soi.next`/`soi.prev`) | none |
| Cycle guns / missiles / bombs | No | one `CommandDispatcher` entry each → existing `WeaponSelectors.Cycle*` |
| Flares / radar jammer | No | one entry → existing `Keybinds.Drive` (make internal) |
| Dedicated gear up / down | No | one entry → existing `Keybinds.DriveGear` (make internal) |
| MAP/TGT highlight nav (`tgt-next`/`tgt-prev`/etc.) | No | one entry → existing `TelemetryServer.MapAction` |
| SOI nav up/down/select (one-shot) | No | one entry → existing `TelemetryServer.SoiAction` |
| Cursor move / cursor select | No | **held-state, not one-shot** — see below, not just plumbing |
| Gun Trigger / Weapon Release / Jammer Pod fire | No | needs a hold-vs-tap design, see below — not just plumbing |

Nothing in this table requires new Harmony patches or previously-untouched game systems — every
row bottoms out in a method NOXMFD's own keybind path already calls. Two rows, though, aren't
one-shot the way the rest of the table is — see "Held-state actions" below before assuming
`keydown -> sendCommand` covers everything.

### Held-state actions: cursor move and cursor select

`Keybinds.Poll()` doesn't send a discrete cursor "move" command — it computes a live vector and
held flag every single frame and pushes both continuously: `TelemetryServer.SetCursorVector(cx,
cy)` and `TelemetryServer.SetCursorSelectHeld(...)` (`Keybinds.cs:673,678`). A page consuming the
cursor needs to see the held flag go `true -> false` to distinguish a tap from a hold
(`docs/page-cursor.md`) — an edge-only "the key was pressed" event can't express that.

So this pair can't be wired as `keydown -> sendCommand('cursor.move')` the way the one-shot rows
above can. The WSO client needs explicit `keydown`/`keyup` pairs that set/clear local held-key
state, then send the resulting vector/held-flag continuously (or on every change) for as long as
the relevant keys are down — mirroring what `Poll()` does every frame, just driven by browser key
state instead of `Input.GetKey`. This is closer in shape to the fire-action problem below than to
the rest of the gap table, and should be scoped and tested as its own piece, not assumed to fall
out of the generic `id -> cmd` mapping the simpler rows use.

### Fire actions (Gun Trigger / Weapon Release / Jammer Pod)

`WeaponSelectors.FireGun/FireRelease/FireJammerPod` are driven every frame from `Keybinds.Poll()`
and use frame-gap tracking (`_gunFrame`/`_relFrame`) to tell a held key from a tapped one. A single
`/command` POST is an edge, not a level — it can't represent "held." Two ways to bridge that,
neither requiring new game-side work:

1. **Client-managed hold-repeat.** The WSO browser sends the fire command once per `keydown` (or
   its own repeat interval) for as long as the key is physically held, and stops on `keyup` — same
   shape as a keyboard's own OS-level key-repeat, just carried over HTTP instead of into
   `Input.GetKey`. Simplest to build, but weaker than it first looks: `FireGun`/`FireRelease`
   (`WeaponSelectors.cs:170-` onward) infer "held" from **consecutive Unity frames**
   (`_gunFrame`/`_relFrame` gap tracking), not from an explicit flag. Browser-cadence HTTP repeats
   arriving as discrete POSTs, each one processed on whatever main-thread frame it happens to land
   on, don't naturally reproduce "consecutive frame" — without care, the game may read each repeat
   as a fresh tap rather than a continued hold, defeating the whole point.
2. **Explicit start/stop commands.** `keydown` sends `fire.start`, `keyup` sends `fire.stop`, and
   `CommandDispatcher` keeps its own tiny per-action held-state flag that's read every frame
   (main-thread side, same cadence `Poll()` already runs at) rather than being event-driven —
   giving `WeaponSelectors` the same "is this held right now" signal shape it already expects,
   just sourced from network state instead of `Input.GetKey`. More plumbing than option 1, but
   avoids reshaping frame-gap-sensitive logic to tolerate irregular network-timed repeats.

**Leaning toward option 2** given the frame-gap dependency above — option 1's appeal (simpler)
trades against a real risk of subtly-wrong fire behavior (missed shots, unintended taps) that's
easy to miss in casual testing and only shows up under real network jitter. Either is buildable
without touching `WeaponSelectors` itself. Not a blocker for shipping the rest of the feature —
target/weapon-select/master-arms/etc. are useful on their own even before fire actions are wired
up, so this can land as a phase-2 addition.

## Same-PC redundancy: the toggle's central risk

NOXMFD's own keybind system doesn't listen through a browser at all — `Keybinds.Poll()` reads
`Input.GetKey`/`GetKeyDown` **directly, in the game process**, on whatever PC the game is running
on, regardless of whether any browser page is even open. That's the pilot's existing input path,
and it is always active on the game's own PC.

So the redundancy risk isn't really "two WSO browsers might conflict" (that's the separate,
smaller concern below) — it's that **the game PC itself is already a live keybind listener**. If
someone opens a WSO-mode browser tab *on that same PC* (testing locally, or a second monitor
driven by the same machine) and binds it to the same keys the pilot's physical keyboard already
triggers, every keypress fires twice: once through `Input.GetKey` in the game process, once
through the WSO tab's `keydown` → `/command` round trip. That's a confusing, hard-to-diagnose bug
for exactly the audience least likely to expect it — someone testing the feature on their own PC
before handing it to a remote WSO.

**Detection approach**: `/soi-instances` (`MfdInstance.Remote`, `TelemetryServer.cs:192`) is
useful *precedent* — it shows the server already tracks each connection's remote address — but
it's not the mechanism to build on directly: it only lists browsers with an open SSE `/stream`
connection, and a KEY page fetching `/keybinds-config` for the WSO toggle has no reason to also
hold a `/stream` open, so it wouldn't reliably show up there. The primary source should be
simpler and self-contained: `ServeKeybindsConfig` (`TelemetryServer.cs:1021`) already runs once
per `/keybinds-config` request and has direct access to `ctx.Request.RemoteEndPoint` for *that*
request — compute the same-PC check right there (loopback `127.0.0.1`/`::1`, or a match against
the host machine's own local network interface addresses, `System.Net.NetworkInterface`
enumerated once at startup) and add it as one more field on the response. No dependency on
`/soi-instances` or on the requesting page having any other connection open.

When that's true, the WSO toggle's page shows a clear, hard-to-miss warning before the toggle can
be switched on — not necessarily a hard block (someone might have a real reason to test locally
with the game's own keyboard unplugged/unfocused), but framed strongly: *"This browser is running
on the same PC as the game — NOXMFD's own keybinds are already listening here. Turning this on
will likely double-fire your inputs."*

This heuristic isn't perfect (a VPN, a second NIC, or genuinely unusual network topology could
produce a false negative), so document it as best-effort in the UI copy rather than a guarantee —
"probably the same PC," not "definitely."

## Multiple senders: conflict and desync analysis

Two or more people (or the pilot's own native input plus a WSO's remote commands) sending actions
concurrently doesn't introduce a new race class — `CommandDispatcher.Drain()` already runs every
queued command sequentially on the main thread each frame, and every handler already assumes
"could be triggered from more than one source" (a HOTAS bind and its keyboard fallback already
fire the same action independently today, and nothing breaks — see `Keybinds.cs`'s own "fires if
EITHER source is active" model). Concretely, for the actions in the gap-analysis table:

- **Set-style commands** (`target.select`, `weapon.select`, `tgt.set`, `combat-mode.set`,
  `master-arms.set`) carry the desired end state explicitly (which target, which weapon, on vs.
  off) — two people sending the same value twice is a true no-op, and two people sending
  *different* values just means the second POST processed wins, same as if one person changed
  their mind twice. Safe by construction; the only "conflict" is a **gameplay** one (two people
  wanting different targets/weapons active), not a software bug.
- **Toggle-style commands** (`avn.toggle` for radar/engine/gear/guns-linked/turret) carry no
  target state at all — each POST just flips whatever the current value is. This is the one place
  where two senders can genuinely **cancel each other out**: if the WSO and the pilot's native
  keybind both toggle the same thing within the same frame window (or even a second apart, without
  either seeing the other's action), the net effect can be "it looks like nothing happened" or
  "it ended up in the wrong state," and neither side gets a clear signal why. Worth naming
  explicitly in the toggle's warning copy — not because it needs a code fix (the ordinary
  last-write-wins behavior is correct and matches how two physical keybinds already interact
  today), but because "toggle" conflicts are more confusing to debug in the moment than "set"
  conflicts, since there's no wrong *value* to point at, just an unexpected state flip.
- **Fire actions** — whichever hold-tracking shape is chosen above, two independent senders
  holding "fire" simultaneously is exactly the existing "either source active" model already
  tolerates for HOTAS + keyboard. No new design needed there either, once the single-sender
  hold-vs-tap shape is settled.
- **No cross-command ordering guarantee beyond arrival order** — commands from two different
  browsers interleave in whatever order their POSTs reach the server, with no timestamp
  reconciliation. For the action set in scope here (discrete selections and toggles, not
  continuous control), this is fine; flag as an explicit non-goal if a future action needs stricter
  ordering.

**Verdict: no server-side conflict-resolution work needed beyond what already exists.** The real
mitigation is social/UX — the toggle's warning copy should say plainly that enabling this on more
than one browser at once means both people can trigger the same actions, and that's a coordination
question between the pilot and WSO, not something the software arbitrates.

## Toggle UX

- **Off by default.** A pilot upgrading NOXMFD should see no behavior change; this is opt-in per
  browser, not a global setting.
- **Location**: most likely its own small section on the KEY page (`src/web/pages/keybinds/`),
  since it's directly about how that page's own bind registry gets used — not a new top-level nav
  entry. Exact placement is an implementation-time call.
- **Label and copy, brief and instructive** — something in this shape (exact wording at
  implementation time):
  - Label: **"LISTEN FOR KEYBINDS (REMOTE)"**, default OFF.
  - One-line description: *"Lets this browser send your configured keybinds to the game as if
    pressed here — for a second player (WSO) working MAP/TGT from another device."*
  - The same-PC warning (above) shown inline, conditionally, when detected — not a generic
    disclaimer always present, since that would train users to ignore it.
- **Scope consideration for later, not blocking v1**: an allow-list of which binds are exposed to
  remote triggering (e.g. targeting/weapon-select yes, flight-critical toggles maybe not) is a
  reasonable future refinement if this sees real use and someone wants tighter control over what a
  WSO can touch — noted here as a design lever, not designed in detail, since the player's actual
  ask is "let the WSO use the same keybinds," not "restrict them."

## Implementation sketch

1. **`CommandDispatcher` additions** — one entry each for cycle guns/missiles/bombs, flares/jammer,
   dedicated gear up/down, MAP/TGT highlight nav, and one-shot SOI nav up/down/select, each calling
   the existing method (making it `internal` where it's currently `private`). Cursor move/select
   and fire actions are held-state, not one-shot — see their own sections above — and are
   candidates for deferring to a later phase alongside fire actions rather than the v1 pass.
2. **Same-PC detection field** on `/keybinds-config` (or a new tiny endpoint) — compute once per
   request from `ctx.Request.RemoteEndPoint`, compare against loopback + enumerated local
   interface addresses.
3. **New client module**, e.g. `src/web/pages/keybinds/wso-remote.js`, following
   `layout-keybinds.js`'s shape almost exactly: fetch `/keybinds-config` (already polled by the KEY
   page), build a `key -> id` map from `binds`, listen for `keydown`/`keyup`, translate matched
   events into `sendCommand(...)` calls using each bind's `id` — reusing whatever mapping
   `CommandDispatcher` already expects for that action (some binds map 1:1 to a `cmd` name, others
   need a small id→command lookup table since `bind.id` and `cmd` aren't always the same string).
4. **Toggle wiring**: a `ConfigEntry<bool>` isn't actually needed server-side for *this* toggle —
   unlike `RatesConfig`'s settings, "does this browser listen" is inherently per-browser client
   state (a checkbox + `localStorage`, not a plugin-persisted value), since the whole point is that
   different browsers make different choices. No new `RatesConfig`-style plugin setting, just a
   client-side on/off persisted the way other per-browser UI prefs already are in this codebase.
5. **KEY page UI**: the toggle, label, description, and the conditional same-PC warning from
   `/keybinds-config`'s new field.

## Open questions to settle while implementing

- Exact id→command mapping for binds whose `Keybinds.cs` id doesn't match a `CommandDispatcher`
  `cmd` string 1:1 — needs a small lookup table in the new client module, not solved by this doc.
- Whether fire actions ship in v1 or as a phase-2 follow-up (leaning phase 2, per "Fire actions"
  above).
- Whether the same-PC warning should be dismissible-and-remembered (`localStorage`) or reappear
  every time the toggle is touched — err toward reappearing, since the risk is about a mistake
  made once per session, not a nag.
- Whether an allow-list of exposed binds is worth building now or left for later (see "Toggle UX").

## Out of scope

- Reading the game's own native Rewired keybind configuration. The WSO doesn't need "the pilot's
  actual in-game binds" in that sense — NOXMFD's own bind registry (`/keybinds-config`) is a
  materially smaller, already-networked surface, and is what "the same keybinds the main player
  defined" means in practice for every action this feature covers.
- Any server-side arbitration of who's "allowed" to send a given command — per "Multiple senders"
  above, this is a social/coordination question, not a software one, for the action set in scope.
- Authentication or per-client trust levels on `/command` generally — a bigger, separate change
  this feature doesn't need to wait on (the existing LAN-trust model already covers this
  feature's addition the same way it covers every existing command sender).
- Building the fire-action hold-vs-tap mechanism as part of the initial pass (see "Fire actions").

## Pre-flight before implementing

- Read `src/web/shell/layout-keybinds.js` in full — it's the shape this feature's client module
  should follow almost exactly, just paired with `sendCommand` instead of a client-only action.
- Read `src/plugin/Keybinds.cs`'s `Poll()`/`PollTapHold()` to confirm the exact fire-action
  frame-gap logic before choosing between the two hold-vs-tap options above.
- Read `TelemetryServer.cs`'s `MfdInstance`/`/soi-instances` handling (`TelemetryServer.cs:179-`)
  before adding the same-PC detection field — reuse its remote-address plumbing rather than
  building a second copy.
