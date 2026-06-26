# Inbound command channel — architecture

Status: **design** (the map tap-to-target POC is shipped; this generalizes it).

## Why

Until the tap-to-target feature, NO XMFD was strictly read-only: it streams
telemetry *out* (SSE) and serves images. Tap-to-target added the first inbound
*write* path — the web client asking the game to do something. That POC is
deliberately ad-hoc (one endpoint, a plain-text body, a `Queue<uint>`). Before
we add more write features (deselect gestures, weapon selection, countermeasure
selection, …) we want a single, well-shaped command channel they all ride, so we
solve marshaling/validation/gating/auth *once*.

## What exists today (the POC)

The shipped tap-to-target path, end to end:

- **Client** (`ClientPage.cs`, MAP page): a tap picks the nearest not-yet-selected
  contact and `POST`s its unit id to `/select` (body is the decimal id as text).
- **Endpoint** (`TelemetryServer.HandleSelect`): gated by `AllowInput` (config
  `Experimental > MapClickTargeting`); returns 403 when off. Validates the id and
  enqueues it under a lock into `Queue<uint> _selectQueue`. Runs on an
  `HttpListener` threadpool thread — must not touch game state here.
- **Main-thread drain** (`TelemetryReader.Update → DrainInputCommands`): dequeues,
  resolves the id via `UnitRegistry.TryGetUnit`, and acts through
  `CombatHUD.SelectUnit` (the game's own high-level targeting), falling back to
  `weaponManager.AddTargetList` when the HUD isn't tracking that contact.
- **Identity**: every `UnitInfo` carries `Unit.persistentID.Id` (a `uint`),
  serialized into the SSE contacts so a click can name a specific unit back.

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
4. **Gate behind explicit config; ship dark.** Inbound write paths default OFF.
5. **Stay legitimate.** Only act on what the player already legitimately knows
   (fog-of-war respected) and only via sanctioned game APIs that replicate through
   the game's own netcode. We never inject network messages or do anything the
   player couldn't do from their own cockpit. This is the hard boundary for
   multiplayer safety.

## Proposed design

### Command envelope

Replace the plain-text body with a small JSON envelope:

```json
{ "cmd": "target.select", "args": { "id": 1234 } }
```

- `cmd` — namespaced verb (`target.*`, `weapon.*`, `cm.*`).
- `args` — per-command payload.
- (later) optional `seq` for client-side correlation / acks.

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

- Today: one master switch (`AllowInput`). Decide whether to keep a single gate
  or split per-capability (targeting vs. weapon control vs. countermeasures).
- The legitimacy boundary (principle 5) is the real safety model: every command
  must map to an action the player could perform from their own cockpit, on their
  own aircraft. Document this per command.

### Client side

- A shared `sendCommand(cmd, args)` helper (today only the MAP posts). Promote to
  a shared snippet once a second page needs it.

## Candidate commands (roadmap)

| Command | Game API | Notes |
|---|---|---|
| `target.select { id }` | `CombatHUD.SelectUnit` | **DONE** (POC) |
| `target.deselect { id }` | `CombatHUD.DeSelectUnit` | needs a deselect gesture (see Q3) |
| `target.clear` | `CombatHUD.DeselectAll(true)` | clears all targets, with audio |
| `target.deselectLast` | `CombatHUD.DeselectLast` | drops most-recent target |
| `weapon.next` / `weapon.prev` | `WeaponManager.NextWeaponStation` / `Previous…` | cycle stations |
| `weapon.select { station }` | `Aircraft.SetActiveStation` | jump to a station |
| `cm.select { category }` | countermeasure manager | pick flares/EW/chaff |

Each new command, before it lands: confirm a public/high-level game method
exists, confirm it's the player's own legitimate cockpit action, then add a
handler + a client trigger.

## Open questions / decisions

1. **Gating** — single master switch, or per-capability flags?
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

1. Generalize `Queue<uint>` → a typed command queue; `/select` → `/command`.
2. Move the select logic into a `target.select` handler behind the dispatcher.
3. Add the next command (likely `target.clear` or a weapon-station cycle) as the
   first *new* feature proving the channel generalizes.
