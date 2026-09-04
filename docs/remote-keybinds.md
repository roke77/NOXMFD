# Remote keybind listening from a second browser

## Status

**V1 built and merged to `main`.** Requested after a player asked about a two-person cockpit setup —
one person flying, a second person ("WSO," weapons systems officer) connected to NOXMFD from a
separate device over the LAN, working MAP/TGT/weapon-selection. That's the scenario that
surfaced the idea, but the feature itself is generic: **any** browser, on any device, opting in to
translate its own keydown/keyup events into the same commands NOXMFD's own keybinds already
trigger. A two-person WSO setup is one use case; a single player who wants to trigger actions from
a second device (a tablet next to the keyboard, a phone, a second PC) without touching the game
PC's own input is another, equally in scope. Nothing about the design below is WSO-specific — it's
written for "a browser, opted in" throughout, with WSO used only where an example is useful.

**Every bind on the KEY page now has a remote path**, except the handful noted at the very end of
this doc as not making sense to relay at all (axis-only binds with no keyboard equivalent, and the
two client-side-modal Layout binds). V1 covered weapon cycling, countermeasure deploy, dedicated
gear up/down, MAP/TGT/SOI one-shot actions, master arm, radar/engine set, combat-mode tap actions,
HUD preset loads, and remote MAP/TGT cursor movement/select/fire. Cursor and fire were intentionally
built outside the simple `keydown -> /command` map because they need live held-state semantics; two
later passes (issue #77 and its full-parity follow-up, both logged inline below) closed every gap
that surfaced afterward — Single Target Weapon Release, Power ON/OFF, Cursor Deselect, TD's 9
Assign binds, Cursor Zoom In/Out, and the rest of the TGP Keybinds group.

**Update (issue #77):** Single Target Weapon Release (issue #68) shipped after this doc's fire
held-state design and was left out of it on purpose ("no remote/PWA counterpart for this pass" —
see `Keybinds.cs`'s original comment on that bind). That made it the one combined-fire bind not
actually driven by `Keybinds.Poll()`'s remote-or-local check, so a remote press of its configured
key silently did nothing while every other fire bind worked. It's now a fourth `fire.set` group
(`"release-single"`), following gun/release/jammer-pod exactly: `RemoteInputState` tracks it with
its own TTL/min-press fields, `Keybinds.Poll()` ORs it into `WeaponSelectors.FireReleaseSingle` and
adds the bind to `IsCombinedFireBind` so the generic Drive loop doesn't also fire it, and
`remote-keybinds.js`'s `fireRoleForBind`/`fireGroupsFromActive` map `weapon-release-single` to that
group the same way the other three are mapped.

## Goal

Let a browser optionally listen for keydown/keyup events and translate them into the same
`/command` POSTs NOXMFD's own bind registry already understands — so any connected browser can use
the same key layout configured on the KEY page, from whatever device that browser happens to be
running on, without needing physical access to the game PC's keyboard or HOTAS.

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
  same registry the KEY page renders and the same data `src/web/shell/shared/layout-keybinds.js` already
  fetches to match a raw browser `KeyboardEvent` against one configured key (for SAVE/LOAD LAYOUT,
  a client-side-only action). That module is the direct precedent for this feature's steps (a) and
  (b) — fetch the table, match keydown against it — just not yet paired with a `sendCommand` call.
- **Most of the actions this feature targets are already invokable via `/command` today**:
  `target.select`, `target.deselect`, `weapon.select`, `master-arms.set`, `combat-mode.set`,
  `avn.toggle` (radar, gear, engine, guns-linked, turret), the full `tgt.*` filter panel,
  `soi.next`/`soi.prev`. No new game-side plumbing needed for these — a keydown handler can call
  `sendCommand` today.
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

## Gap analysis: command-channel coverage for the actions this feature targets

| Action | `/command` today? | Work needed |
|---|---|---|
| Target select / deselect | Yes | none |
| Weapon select (by name) | Yes | none |
| Master Arm on/off | Yes | none |
| Combat mode A/A · A/G · ALL | Yes | none |
| Radar / engine / gear / guns-linked / turret toggle | Yes (`avn.toggle`) | none |
| TGT filter set/only/reset/clear/datalink/stale/laser/hud | Yes (`tgt.*`) | none |
| SOI next/prev | Yes (`soi.next`/`soi.prev`) | none |
| Cycle guns / missiles / bombs | No | one `CommandDispatcher` entry each → existing `WeaponSelectors.Cycle*` |
| Flares / radar jammer | No | one entry → existing `Keybinds.Drive` (make internal) |
| Dedicated gear up / down | No | one entry → existing `Keybinds.DriveGear` (make internal) |
| MAP/TGT highlight nav (`tgt-next`/`tgt-prev`/etc.) | No | one entry → existing `TelemetryServer.MapAction`, plus `Keybinds.CycleTargetFocus` for `tgt-next`/`tgt-prev` specifically (issue #62, docs/tgt-cycle-focus.md) |
| SOI nav up/down/select (one-shot) | No | one entry → existing `TelemetryServer.SoiAction` |
| Cursor move / cursor select | Yes (`cursor.set` / `cursor.select`) | built as remote held-state merged into the existing cursor stream |
| Gun Trigger / Weapon Release / Single Target Weapon Release / Jammer Pod fire | Yes (`fire.set`) | built as remote held-state merged into `Keybinds.Poll()` |

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

This is now built as a separate held-state path rather than folded into the one-shot id -> command
map. The remote client keeps browser `keydown`/`keyup` state for `cursor-up/down/left/right/select`,
sends the current vector through `cursor.set` on change and as a short keepalive, and emits
`cursor.select` once on the select key's down edge. The server stores that remote cursor source with
a short expiry, and `Keybinds.Poll()` merges it with the physical keyboard/axis state before
calling `TelemetryServer.SetCursorVector(...)` and `SetCursorSelectHeld(...)`. That keeps the
existing SSE cursor transport authoritative while avoiding a stuck cursor if the browser tab closes
or drops a keyup.

### Fire actions (Gun Trigger / Weapon Release / Single Target Weapon Release / Jammer Pod)

`WeaponSelectors.FireGun/FireRelease/FireReleaseSingle/FireJammerPod` are driven every frame from
`Keybinds.Poll()` and use frame-gap tracking to tell a held key from a fresh press. A single
`/command` POST is an edge, not a level, so the implementation uses explicit held state: the
remote browser sends `fire.set` true on keydown, false on keyup, plus a short keepalive while
held. The server stores independent short-lived flags for gun, release, release-single, and
jammer-pod, and `Keybinds.Poll()` merges those flags with the local binds before calling each
`WeaponSelectors` fire method at most once per frame.

**Update (issue #77 audit):** Power ON/OFF and Cursor Deselect were added to NOXMFD after this
doc's original coverage pass and were missing from `remote-keybinds.js` the same way Single Target
Weapon Release was — not held-state, just plain one-shot mappings nobody had added yet. Power
ON/OFF needed one new `CommandDispatcher` entry (`power.set`, mirroring `master-arms.set` exactly);
Cursor Deselect needed none — it was already a `TelemetryServer.MapAction` broadcast, the same
shape `tgt-datalink`/`tgt-stale` already relay, so it only needed the `commandForBind` case.

**Update (full-parity pass):** every remaining bind on the KEY page now has a remote path, closing
the two gaps this doc used to list here:
- **TD's 9 Assign binds** (`td-assign-1`..`9`, squad-leader-only, issue #47) send `td.assign` with
  `{index, on: false}` — always the tap outcome (assign-and-clear-selection), never the hold
  outcome (assign-and-retain-for-chaining, `TdStore.Assign(slot, retain: true)`). A remote keydown
  has no hold detection to offer a second outcome, same accepted limitation as Combat Mode A/A ·
  A/G below only remoting their tap behavior.
- **Cursor Zoom In/Out** joined `fire.set` as two more held groups (`zoom-in`/`zoom-out`) —
  `RemoteInputState`'s fire tracking was generalized from a fixed field quad per group into one
  `Dictionary<string, ...>` table precisely so this (and any future held bind) didn't need new
  fields, just a new group name.
- **The rest of the TGP Keybinds group** (Manual Control Toggle, Manual Control Reset, Point
  Track, Snap To Head Tracker, Toggle IR, Mark Steer Point, both Full Screen toggles) are all
  one-shot `commandForBind` entries now. Point Track/Manual Reset/Mark Steer Point reuse the exact
  `/command` names the TGP page's own TRK/RST/STP bezel buttons already sent; the other five needed
  one new `CommandDispatcher` entry each (`tgp.manual-toggle`, `tgp.ir-toggle`,
  `tgp.snap-headtracker`, `tgp.fullscreen-toggle`, `tgp.fullscreen-hud-toggle`) — Toggle IR/Manual
  Toggle are blind flips rather than reusing the existing `tgp.ir.set`/`tgp.manual.set` explicit-
  state commands, since a remote browser has no reliable read of current state to send the
  opposite of.

**Binds that stay unrelayed, on purpose:** Cursor Horizontal/Vertical and Cursor Zoom Axis are
axis-only — no `key` field exists for a browser keydown to match in the first place, since they
represent a continuous HOTAS axis a keyboard tap can't produce (Cursor Left/Right/Up/Down/the
zoom-in/out pair above are the discrete keyboard-shaped equivalents, and those are relayed). SAVE
LOAD LAYOUT's two binds pop a client-side modal in whatever browser is looking at the KEY page —
there's no server-side action to relay at all, and triggering a text-entry modal on a *different*
browser than the one physically being typed into wouldn't make sense as "remote" in the first
place.

That preserves the existing two-stage switch-then-fire behavior and avoids relying on HTTP repeat
cadence to mimic Unity frame continuity. The server-side expiry is the safety valve: if a browser
tab closes or a keyup is lost, the held flag drops back to false automatically. Browser taps can
deliver `true -> false` between Unity frames, so `fire.set` uses a tiny minimum press window before
honoring release; `Keybinds.Poll()` also includes remote fire in its early-return check so
remote-only presses are not skipped while all local binds are idle.

## Same-PC redundancy: the toggle's central risk

NOXMFD's own keybind system doesn't listen through a browser at all — `Keybinds.Poll()` reads
`Input.GetKey`/`GetKeyDown` **directly, in the game process**, on whatever PC the game is running
on, regardless of whether any browser page is even open. That's the existing input path for
whoever is at the game PC's own keyboard/HOTAS, and it is always active on that PC.

So the redundancy risk isn't really "two remote browsers might conflict" (that's the separate,
smaller concern below) — it's that **the game PC itself is already a live keybind listener**. If
someone opens a remote-listening browser tab *on that same PC* (testing locally, or a second
monitor driven by the same machine) and it's bound to the same keys the physical keyboard already
triggers, every keypress fires twice: once through `Input.GetKey` in the game process, once
through the browser tab's `keydown` → `/command` round trip. That's a confusing, hard-to-diagnose
bug for exactly the audience least likely to expect it — someone testing the feature on their own
PC before handing a second device to a WSO, or a solo player who forgets they left a second local
browser tab in this mode.

**Detection approach**: `/soi-instances` (`SseHub.MfdInstance.Remote`) is
useful *precedent* — it shows the server already tracks each connection's remote address — but
it's not the mechanism to build on directly: it only lists browsers with an open SSE `/stream`
connection, and a KEY page fetching `/keybinds-config` for this toggle has no reason to also hold
a `/stream` open, so it wouldn't reliably show up there. The primary source should be simpler and
self-contained: `ConfigEndpoint.ServeKeybindsConfig` already runs once per
`/keybinds-config` request and has direct access to `ctx.Request.RemoteEndPoint` for *that*
request — compute the same-PC check right there (loopback `127.0.0.1`/`::1`, or a match against
the host machine's own local network interface addresses, `System.Net.NetworkInterface`
enumerated once at startup) and add it as one more field on the response. No dependency on
`/soi-instances` or on the requesting page having any other connection open.

When that's true, the page shows a clear, hard-to-miss warning before the toggle can be switched
on — not necessarily a hard block (someone might have a real reason to test locally with the
game's own keyboard unplugged/unfocused), but framed strongly: *"This browser is running on the
same PC as the game — NOXMFD's own keybinds are already listening here. Turning this on will
likely double-fire your inputs."*

This heuristic isn't perfect (a VPN, a second NIC, or genuinely unusual network topology could
produce a false negative), so document it as best-effort in the UI copy rather than a guarantee —
"probably the same PC," not "definitely."

## Multiple senders: conflict and desync analysis

Two or more people (or one player's native input plus a second browser's remote commands) sending
actions concurrently doesn't introduce a new race class — `CommandDispatcher.Drain()` already runs
every queued command sequentially on the main thread each frame, and every handler already assumes
"could be triggered from more than one source" (a HOTAS bind and its keyboard fallback already
fire the same action independently today, and nothing breaks — see `Keybinds.cs`'s own "fires if
EITHER source is active" model). Concretely, for the actions in the gap-analysis table:

- **Set-style commands** (`target.select`, `weapon.select`, `tgt.set`, `combat-mode.set`,
  `master-arms.set`) carry the desired end state explicitly (which target, which weapon, on vs.
  off) — two senders sending the same value twice is a true no-op, and two senders sending
  *different* values just means the second POST processed wins, same as if one person changed
  their mind twice. Safe by construction; the only "conflict" is a **gameplay** one (two people
  wanting different targets/weapons active), not a software bug.
- **Toggle-style commands** (`avn.toggle` for radar/engine/gear/guns-linked/turret) carry no
  target state at all — each POST just flips whatever the current value is. This is the one place
  where two senders can genuinely **cancel each other out**: if two sources both toggle the same
  thing within the same frame window (or even a second apart, without either seeing the other's
  action), the net effect can be "it looks like nothing happened" or "it ended up in the wrong
  state," and neither side gets a clear signal why. Worth naming explicitly in the toggle's warning
  copy — not because it needs a code fix (the ordinary last-write-wins behavior is correct and
  matches how two physical keybinds already interact today), but because "toggle" conflicts are
  more confusing to debug in the moment than "set" conflicts, since there's no wrong *value* to
  point at, just an unexpected state flip.
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
than one browser at once means both sources can trigger the same actions, and that's a
coordination question between whoever's controlling each browser, not something the software
arbitrates.

## Toggle UX

- **Off by default.** A pilot upgrading NOXMFD should see no behavior change; this is opt-in per
  browser, not a global setting.
- **Location**: originally placed inside the KEY page's **IMMERSION OPTIONS** section, alongside
  ENABLE RADAR/ENGINE/MASTER ARM ON START and FORCE HUD FILTERS ON COMBAT MODE — visually
  consistent with its neighbors, but **behaviorally different** in one respect: every other row in
  that section is a server-persisted `ImmersionConfig` setting (`radarOnOnStart` etc., shared
  across every browser that connects), while this toggle is deliberately per-browser `localStorage`
  state (see "Toggle wiring" below) — the same PC's second browser tab, or a different device
  entirely, should NOT inherit whatever this toggle is set to elsewhere. That tension (a
  local-only, client-side toggle sitting in a section whose other rows are all shared/global) is
  why it later moved to the top of the page instead, right after INPUT WHEN GAME UNFOCUSED — the
  page's other client-only, non-bind setting — leaving IMMERSION OPTIONS to hold only real
  server-persisted settings.
- **Label and copy, brief and instructive, generic rather than WSO-specific**:
  - Label: **"LISTEN FOR KEYBINDS (REMOTE)"**, default OFF.
  - One-line description: *"Lets this browser send your configured keybinds to the game as if
    pressed here — for controlling from a second device, solo or with someone else at the
    controls."*
  - The same-PC warning (above) shown inline, conditionally, when detected — not a generic
    disclaimer always present, since that would train users to ignore it.
- **Scope consideration for later, not blocking v1**: an allow-list of which binds are exposed to
  remote triggering (e.g. targeting/weapon-select yes, flight-critical toggles maybe not) is a
  reasonable future refinement if this sees real use and someone wants tighter control over what a
  remote browser can touch — noted here as a design lever, not designed in detail, since the actual
  ask is "let a second browser use the same keybinds," not "restrict them."

## V1 implementation shape

1. **`CommandDispatcher` additions** — one entry each for cycle guns/missiles/bombs, flares/jammer,
   dedicated gear up/down, MAP/TGT highlight nav, one-shot SOI nav up/down/select, and cursor
   edge/held-state commands, plus `fire.set` for the remote fire held-state holder. These call the
   existing method or `RemoteInputState` holder, making helpers `internal` where they were previously `private`.
2. **Same-PC detection field** on `/keybinds-config` — compute once per
   request from `ctx.Request.RemoteEndPoint`, compare against loopback + enumerated local
   interface addresses.
3. **New client module**, `src/web/services/remote-keybinds.js`: the top-level copy bootstraps
   `/keybinds-config` once, consumes later SSE updates, and distributes the current snapshot to
   child documents; every copy builds a `key -> id` map from `binds`, listens for `keydown`/`keyup`, and translates matched
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

## Follow-up questions

- Whether the V1 id→command mapping should grow an allow-list UI later; the current fixed lookup
  lives in `src/web/services/remote-keybinds.js`, with cursor held-state kept in its own lookup,
  and both are pinned by `remote-keybinds.test.js`.
- Whether the same-PC warning should ever become dismissible-and-remembered (`localStorage`). It
  currently reappears because the risk is about a mistake made once per session, not a nag.
- Whether an allow-list of exposed binds is worth building now or left for later (see "Toggle UX").

## Out of scope

- Reading the game's own native Rewired keybind configuration. A remote browser doesn't need "the
  in-game player's actual native binds" in that sense — NOXMFD's own bind registry
  (`/keybinds-config`) is a materially smaller, already-networked surface, and is what "the same
  keybinds already configured" means in practice for every action this feature covers.
- Any server-side arbitration of who's "allowed" to send a given command — per "Multiple senders"
  above, this is a social/coordination question, not a software one, for the action set in scope.
- Authentication or per-client trust levels on `/command` generally — a bigger, separate change
  this feature doesn't need to wait on (the existing LAN-trust model already covers this
  feature's addition the same way it covers every existing command sender).
- Changing `WeaponSelectors`' two-stage switch-then-fire model; remote fire feeds that existing
  model instead of redefining it.

## Relevant implementation references

- `src/web/shell/shared/layout-keybinds.js` is the closest existing browser-side keydown pattern; the
  remote listener uses the same key-name vocabulary but sends `/command` actions instead of opening
  a client-only modal.
- `src/plugin/Input/Keybinds.cs`'s `Poll()`/`PollTapHold()` defines the local held-state and tap/hold
  behavior that remote cursor/fire state must merge into.
- `SseHub.cs`'s `MfdInstance`/`/soi-instances` handling is the nearby precedent for
  server-observed remote addresses; `/keybinds-config` uses the request address directly for the
  same-PC warning.
