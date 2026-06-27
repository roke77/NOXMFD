# Inbound command channel — architecture

Status: **landed (v1)** — the generic channel is in place and tap-to-target rides
it as `target.select`. Single master gate, fire-and-forget, dedicated dispatcher.
Remaining items below are the command roadmap and the open questions.

## Why

Until the tap-to-target feature, NO XMFD was strictly read-only: it streams
telemetry *out* (SSE) and serves images. Tap-to-target added the first inbound
*write* path — the web client asking the game to do something. That POC is
deliberately ad-hoc (one endpoint, a plain-text body, a `Queue<uint>`). Before
we add more write features (deselect gestures, weapon selection, countermeasure
selection, …) we want a single, well-shaped command channel they all ride, so we
solve marshaling/validation/gating/auth *once*.

## What exists today (the channel)

The end-to-end path, after the v1 refactor:

- **Client** (`ClientPage.cs`): `sendCommand(cmd, args)` `POST`s a JSON envelope to
  `/command`. The MAP tap handler picks the nearest not-yet-selected contact and
  sends `target.select { id }`.
- **Endpoint** (`TelemetryServer.HandleCommand`): runs on an `HttpListener`
  threadpool thread, so it only parses (`JsonUtility.FromJson<CommandEnvelope>`) +
  validates + enqueues — never touches game state. Responses: 204 accepted · 400
  malformed · 422 unknown cmd. Queue is depth-capped (`MaxQueuedCommands`). The
  channel is **built-in / always live** (no config gate) — every command maps to
  a legitimate player action on their own aircraft.
- **Dispatcher** (`CommandDispatcher`, drained from `TelemetryReader.Update`):
  on the main thread, looks up the `cmd` in its handler registry and runs it.
  Unknown → logged + dropped; handler exceptions are caught + logged. The
  `target.select` handler resolves the id via `UnitRegistry.TryGetUnit` and acts
  through `CombatHUD.SelectUnit` (the game's own high-level targeting), falling
  back to `weaponManager.AddTargetList` when the HUD isn't tracking that contact.
- **Identity**: every `UnitInfo` carries `Unit.persistentID.Id` (a `uint`),
  serialized into the SSE contacts so a tap can name a specific unit back.

Adding a command is now: register a handler in `CommandDispatcher._handlers`,
add any new fields to `CommandEnvelope`, and call `sendCommand(...)` from the client.

## Principles (learned building the POC)

1. **Marshal to the main thread.** HTTP handlers run on threadpool threads; all
   Unity/game state must be touched on the Unity main thread. Commands are parsed
   + enqueued on the server thread and *executed* in `Update`.
2. **Route through the game's own high-level input layer, not the lowest-level
   setter.** `weaponManager.RemoveTargetList` dropped the target but left the
   cockpit marker green and silent; `CombatHUD.DeSelectUnit` does the marker
   recolour + beep + DynamicMap sync. Always prefer the method the game's *own*
   input invokes — it bundles the side effects we'd otherwise miss.
3. **Validate at the boundary, act idempotently.** Resolve ids to live units;
   no-op on stale/invalid; never produce duplicate state (e.g. `AddTargetList`
   has no de-dup, so we gate on `CheckIsTarget`).
4. **Prove behind a gate, then graduate.** A new write path can ship behind a
   config toggle while unproven; once validated it becomes built-in (the
   tap-to-target toggle was removed once it was solid). Don't keep a permanent
   blanket switch — gate per-command only if a command needs it (principle 5).
5. **Stay legitimate.** Only act on what the player already legitimately knows
   (fog-of-war respected) and only via sanctioned game APIs that replicate through
   the game's own netcode. We never inject network messages or do anything the
   player couldn't do from their own cockpit. This is the hard boundary for
   multiplayer safety.

## Proposed design

### Command envelope

A small **flat** JSON envelope:

```json
{ "cmd": "target.select", "id": 1234 }
```

- `cmd` — namespaced verb (`target.*`, `weapon.*`, `cm.*`).
- everything else — flat top-level params; each handler reads what it needs.
- (later) optional `seq` for client-side correlation / acks.

**Why flat, not `{cmd, args:{…}}`:** the game's `JsonUtility` reliably populates
top-level fields of a `[Serializable]` class but silently fails to deserialize
nested `[Serializable]` objects in the Mono runtime (it left a nested `args.id`
at 0, so commands parsed but did nothing). Flat side-steps that entirely.

### Transport

- One endpoint: `POST /command`. (The mod is unreleased, so we can drop `/select`
  outright rather than keep an alias.)
- Responses: `204` accepted/queued · `400` malformed · `403` channel disabled ·
  `422` unknown cmd / failed envelope validation. Stays fire-and-forget for now
  (see open question on acks).

### Server side (`TelemetryServer`)

- Parse + minimally validate the envelope on the server thread.
- Enqueue a typed command (cmd id + parsed args) into one lock-guarded queue,
  generalizing today's `Queue<uint>`. Cap the queue depth to bound abuse.

### Main-thread dispatch

- A `CommandDispatcher` drained once per frame (where `DrainInputCommands` is now).
- A registry mapping `cmd` → handler delegate. Each handler runs on the main
  thread, re-validates against live state, performs the action via the highest-
  level game API, and logs the outcome. Unknown cmd → logged + dropped.
- Open: keep this in `TelemetryReader`/`Worker`, or a dedicated component.

### Authority / gating

- **No gate.** The channel started behind a config toggle (`AllowInput`) while it
  was unproven; now that tap-to-target and TGL deselect are validated, it's a
  built-in feature, always live. The toggle was removed.
- The legitimacy boundary (principle 5) is the real safety model: every command
  must map to an action the player could perform from their own cockpit, on their
  own aircraft. Document this per command. If a future command ever falls outside
  that boundary, gate *that* command — don't re-add a blanket switch.

### Client side

- A shared `sendCommand(cmd, args)` helper (today only the MAP posts). Promote to
  a shared snippet once a second page needs it.

## Candidate commands (roadmap)

| Command | Game API | Notes |
|---|---|---|
| `target.select { id }` | `CombatHUD.SelectUnit` | **DONE** (POC) — MAP tap |
| `target.deselect { id }` | `CombatHUD.DeSelectUnit` | **DONE** — TGL page bezel key beside a target (full MFD view; split mode TBD) |
| `target.clear` | `CombatHUD.DeselectAll(true)` | clears all targets, with audio |
| `target.deselectLast` | `CombatHUD.DeselectLast` | drops most-recent target |
| `weapon.next` / `weapon.prev` | `WeaponManager.NextWeaponStation` / `Previous…` | cycle stations |
| `weapon.select { station }` | `Aircraft.SetActiveStation` | jump to a station |
| `cm.select { category }` | countermeasure manager | pick flares/EW/chaff |

Each new command, before it lands: confirm a public/high-level game method
exists, confirm it's the player's own legitimate cockpit action, then add a
handler + a client trigger.

## Open questions / decisions

1. ~~**Gating** — single master switch, or per-capability flags?~~ **resolved:**
   no blanket gate; built-in, gate per-command only if ever needed.
2. **Acks** — keep fire-and-forget, or add a result channel (HTTP response body,
   or a `cmdAck` event over the SSE stream) for commands that can fail visibly?
3. **Deselect UX** — add a map gesture (long-press / right-click → `target.deselect`)
   or keep deselection a cockpit-only action?
4. **Dispatcher home** — `TelemetryReader`/`Worker`, or a dedicated `CommandDispatcher`
   component?
5. **Envelope versioning** — do we version `/command` now for the planned React
   client, or add versioning when it's actually needed?

## Migration from the POC

A pure refactor first (behaviour identical), then new commands layer on:

1. ~~Generalize `Queue<uint>` → a typed command queue; `/select` → `/command`.~~ **done**
2. ~~Move the select logic into a `target.select` handler behind the dispatcher.~~ **done**
3. ~~Add the next command as the first *new* feature proving the channel
   generalizes.~~ **done** — `target.deselect` on the TGL page bezel keys: a
   second command, a second client surface (the MFD shell now has its own
   `sendCommand`), reusing the channel unchanged.
