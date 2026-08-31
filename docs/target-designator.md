# TD: Target Designator

Issue #47 — a squad leader hand-assigns targets from their own live TGT list to specific squad
members over the squad transport (docs/squadron-transport.md). Members get their own TD page
showing only what was designated to them, with a one-tap AQUIRE that selects everything in-game.

## Where the data lives

TD's leader table is not a separate target list — it's the identical `tgt-targets` stream TGT
itself renders (`src/web/services/telemetry-source.js` decodes id/name/grid/range/faction/
datalink from the raw telemetry frame; the shell mirrors it as `targetsData` and forwards it to
whichever page is showing `'tgt'` *or* `'td'`, `mfd.js`/`f35.js`). TD adds nothing server-side to
compute that list — it only owns an overlay on top of it. Unlike TGT, `td.js` deliberately does
NOT redraw on every one of those messages — see "A static table, on purpose" below.

- **`TdStore.cs`** (plugin, 100% BCL — no Squad/Unit/CommandDispatcher touchpoint, same
  testability seam `RouteStore.cs` keeps) holds the leader's in-progress selection
  (`HashSet<uint>`) and per-target slot assignments (`Dictionary<uint, HashSet<int>>`), plus the
  member's last-received designated-target snapshot (`List<Row>`) — replaced wholesale on every
  DESIGNATE, never merged. Served at `GET /td-state` (`{ready, state}`, same shape `GET /squad`
  uses).
- Leader-only gating (`Squad.IsLeader`) and the actual in-game unit selection (AQUIRE) both live in
  `CommandDispatcher.cs`, not `TdStore.cs` — `TdAcquireAll()` sits next to
  `ClearDatalinkTargets`/`ClearStaleTargets` since it needs `Unit`/`UnitRegistry`.

## Wire commands

Every TD command reuses an existing `CommandEnvelope` field — no new ones were added:

| Command | Fields reused | Effect |
| --- | --- | --- |
| `td.select` | `id` | Leader: toggle a row's selection. |
| `td.assign` | `index` (slot), `on` (retain) | Leader: toggle every selected target's membership in that slot, then clear selection unless `on` (a long-press — see below). |
| `td.clear` | — | Leader: wipe selection + assignments. |
| `td.designate` | `peer`, `text` | Leader: `Squad.SendDataTo(peer, "td.designate", text)` — one call per member with 1+ assigned targets. |
| `td.receive-designation` | `text` | Member: replace the designated-target table wholesale. |
| `td.member-clear` | — | Member: empty the designated-target table. |
| `td.acquire-all` | — | Member: select every designated target in-game. |

DESIGNATE itself is composed **in the browser** (`td.js`), not the plugin: the leader's live
target rows are client-side data (see above), so `td.js` filters them down to each member's
assigned ids and fires one `td.designate` POST per member. The plugin never needs the full roster
to fan this out.

## A static table, on purpose (issue #47 follow-up)

The first version of this page redrew its whole target table on every `tgt-targets` message —
the same cadence TGT's live telemetry stream updates at (well under a second). That table is also
a set of click targets (row select, squad-button assign), and a click is a mousedown-then-mouseup
gesture spanning tens of milliseconds; a redraw landing in that window could destroy the element
under the cursor, reposition it, or simply repaint stale state over a click's own visual feedback.
Several rounds of narrower fixes (stable DOM nodes, splitting live-text updates from selection
updates, freezing row position) each removed one way this happened, but the table was still
updating on a timer nothing asked for.

The actual fix: `td.js` doesn't redraw on the feed at all. `liveTargets` is kept current from every
`tgt-targets` message (a plain variable, no DOM write), but `applyLiveTargets()` — the only thing
that touches the table's rows — runs in exactly three cases, all deliberate:
1. **A real select/deselect in-game** — the *set* of locked target ids changed (compared via a
   sorted-ids key), not a pure value-only update (range/grid drifting on an already-locked target).
2. **The REFRESH button** — re-applies whatever the latest stored snapshot is, on demand.
3. **Once, when the leader view first renders** (nothing to show otherwise).

Squad/assignment state (`GET /squad`, `GET /td-state`) follows the same rule — fetched once on
page load and again only from REFRESH, no `setInterval` anywhere in this page.

**Assign: tap vs. long-press.** A squad button is a tap-vs-long-press control, same `LONG_MS`
pointerdown-timer shape TGT's own filter cells already use (`tgt.js`) — no new keybind, no PAD-
cursor-hold plumbing. A tap assigns and clears the selection, as before. A long-press assigns and
keeps the selection lit (`TdStore.Assign(slot, retain: true)`), so a leader can designate the same
selected targets to several squad slots in a row without re-selecting between each one. `td.assign`
sends this as the existing `on` `CommandEnvelope` field so the plugin's own state agrees — otherwise
a REFRESH mid-sequence would silently wipe the highlights the leader is deliberately keeping.

## DESIGNATE returns to TGT, and TGT shows the result

Two small pieces close the loop back to TGT (issue #47 follow-up):

- **DESIGNATE returns the leader to TGT** once it fires. TD has no shell-navigation authority of
  its own — it `postMessage`s a `td-designated` type up to whichever shell is hosting it, which
  looks up which pane/frame actually sent it (TD can be the full-view page or either split pane) and
  calls that display's own page-switch function. mfd.js keeps a canonical-source guard on its
  message handler (only `mapFrame` may post most types) that had to be extended for this one, the
  same way `follow`/`grid`/`wpt-routes-request` already are — TD's iframe is never `mapFrame`.
- **TGT gains a leader-only TD column**, second from the left, showing the same slot number(s)
  `td.js`'s own tags show. TGT has no reason to know about squad state otherwise, so `tgt.js` polls
  `GET /squad` + `GET /td-state` on its own 2s cadence (matching `td-nav.js`'s existing "is this
  pilot in a squad" poll) purely to drive this column — toggling `.has-td-col` on `.tgt-panel` for
  visibility and feeding `assignments` into the id-keyed row-update loop TGT already runs at 10 Hz.
  This intentionally reuses TGT's existing "rebuild rows only when the id-set changes, otherwise
  just refresh text" architecture rather than introducing a new one — TGT was already engineered
  this way from the start, unlike TD's first version.

## Delivery to the member

`Squad.SendDataTo(memberId, type, payload)` is a single-recipient sibling of the existing
`Squad.SendData` (which broadcasts to every member) — same envelope, `Squadron.SendTo` instead of
`SendToAll`. On the receiving end, the payload arrives exactly like a `wpt.route` share: the
shell's `squadron` SSE listener (`telemetry-source.js`) forwards it up, and `applySquadronPayload`
(`mfd.js`/`f35.js`) POSTs `td.receive-designation` — the same pattern `wpt.receive-shared` already
uses for waypoint routes.

## Squad-slot numbering

Reuses the exact scheme `sqd.js`'s roster table already established: slot 1 is the leader (self),
slot `i+2` is `state.members[i]` (join order — `Squad.cs`'s `_members` list only ever appends).
Slot 1 assignments are tag-only, per the issue's own scope — DESIGNATE never sends to yourself.

## Keybinds

`TD Keybinds` — `td-assign-1`..`td-assign-9`, `DriveFree`, edge-triggered, riding the `map-act`
channel exactly the way `TGT Keybinds`' `tgt-datalink`/`tgt-stale` do
(docs/tgt-keybind-nav.md): a direct `TdStore.Assign(slot)` call (gated on `Squad.IsLeader`) plus a
`TelemetryServer.MapAction("td-assign-N")` broadcast for whichever display holds SOI. Only
meaningful on the leader's own TD view while it holds SOI — a member's TD view has no squad
buttons to mirror, so the press is a no-op there, same scoping those existing TGT binds get for
free.

## Nav visibility

TD only appears in TGT's own nav row while the pilot is in a squad (leader or member) — the "New TD
sub-page under TGT" scope item. `nav-model.js`'s `NAV.tgt` keeps a static `[MAIN]`-only baseline;
`src/web/shell/shared/td-nav.js` (mirrors `ext-nav.js`'s "presence discovered at runtime" shape)
polls `GET /squad` every 2s and rewrites `NAV.tgt` in place. Like `ext-nav.js`'s own documented
limitation, a squad joined/left while already on TGT shows up the next time TGT's nav is read, not
instantly.

## Lifecycle cleanup (squad/TD audit follow-up)

An audit of what actually gets cleared when a squad ends, or the pilot returns to the main menu and
starts a new mission, found several gaps specific to TD:

- **Slot renumbering on roster shrink.** `TdStore._assignments` is keyed by target id -> a set of
  *positional* slot numbers (slot = a member's index in `Squad._members` + 2, the same number
  `td.js`'s own `squadSlots()` computes). A kick or a voluntary leave shrinks `_members` without
  touching those slot numbers, so every assignment above the departed member's own slot used to
  silently point at the wrong pilot the next time anything read it (a poll, DESIGNATE). Fixed by
  `TdStore.RenumberAfterMemberRemoved(removedSlot)`, called from `Squad.cs`'s `Kick()` and
  `HandleLeave()` right after the member is actually removed: it drops the departed member's own
  slot from every assignment (removing the target entirely if that was its only slot) and shifts
  every slot above it down by one.
- **Reacting to a disband while TD is already open.** TD deliberately has no polling of its own
  (see "A static table, on purpose" above) — a squad ending while the page sits open had no way to
  reach it. Rather than add a poll (which would reintroduce exactly the churn this page was rebuilt
  to remove), `td-nav.js`'s *existing* 2s `/squad` poll (needed anyway for the TGT nav row above)
  now also detects the true->false "was in a squad, now isn't" edge and dispatches a plain
  `td-squad-ended` window event; `mfd.js`/`f35.js` forward it to whichever pane/frame is actually
  showing `'td'`, and `td.js` reacts by re-running the exact `refreshSquad()`/`refreshTd()` calls its
  own REFRESH button already uses — a one-shot reactive catch-up, not a new timer.
- **Reacting to a fresh designation while TD is already open.** The same gap existed the other
  direction: `applySquadronPayload`'s `td.designate` branch fires `td.receive-designation` so
  `TdStore.cs` updates plugin-side, but with no polling of its own an already-open member view had
  no way to learn the fetch it needs (`/td-state`) had anything new to show — nothing until a manual
  REFRESH. Fixed the same way as the disband case: the shell posts a `td-designation-received`
  message to whichever pane/frame shows `'td'` right after issuing `td.receive-designation`, and
  `td.js` reacts with a one-shot `refreshTd()` — no poll, just an immediate nudge tied to the actual
  event instead of an edge detected by someone else's timer.
- **Mission-boundary cleanup elsewhere** (not TD-specific, but found by the same audit): `Squad.cs`'s
  `ResetToNone()` now also clears `RouteStore`'s shared-route locks (previously missing from
  `RelinquishLeadership`/`Disband`/a leader leaving alone) and the leader's own `_notice` (so a
  disband/kick notice can't re-toast on a later fresh page load, possibly in a different mission);
  `HandleTransfer` clears the promoted successor's own old member-side state, the one squad-ending
  path that doesn't go through `ResetToNone` at all; and `MissionLifecycle.StopReader()` now calls
  `PlayerRoster.Refresh()` so SQD stops showing everyone's last mission's aircraft indefinitely at
  the main menu. Squad membership itself deliberately still survives a mission boundary — menu-time
  squad formation is an intentional, pre-existing feature, not an oversight.

## DESIGNATE silently sending nothing (in-game report)

A live two-machine test reported "leader clicked DESIGNATE, member saw nothing" — no error, no
partial result, just silence. Root cause was entirely in the browser: `designateBtn`'s handler
(`td.js`) read `td.state.assignments` directly instead of `effectiveAssignments(td.state)`, the
helper every other part of the page already goes through. `doAssign()` (the squad-button tap/hold
gesture) only ever updates the `assignmentsOverride` layer for instant UI feedback — TD has no
polling of its own, so the raw fetched `td.state` doesn't catch up until the next REFRESH/nudge.
Assigning a target and clicking DESIGNATE right after — the natural order, with no REFRESH in
between — meant DESIGNATE read stale (often empty) assignments and quietly sent nothing, even
though the tag on the row visibly showed the assignment had worked.

This looked, from the plugin's log, identical to a Squadron transport failure: `Presence.cs`
already logs a `Squadron send to <id> failed: k_EResultConnectFailed` warning every 5 seconds for
any faction-mate not currently reachable, and nothing at all logged for `td.designate` specifically
— so a real send failure and "never even attempted" were indistinguishable from the log alone.
Fixed on two fronts:

- **The actual bug**: `designateBtn`'s handler now reads `effectiveAssignments(td.state)`.
- **The diagnostic gap**: `CommandDispatcher.cs`'s `td.designate` handler (now a named `TdDesignate`
  method, not an inline lambda) logs every outcome — not-leader, unparsed/non-member peer, or a
  `sent`/`not sent` result from `Squad.SendDataTo` with the target count — so a future report can
  tell from the leader's own log alone whether DESIGNATE was even attempted, and by whom it was
  rejected. `TdStore.ReceiveDesignation` (member side) logs a receipt count through the same
  BepInEx-free `Action<string>?` hook `RouteStore.LogWarning` already uses (wired in `Plugin.cs`),
  so the member's own log independently confirms arrival — decoupled from whether that pilot's TD
  page happened to be open to see it.

## Verification

`dotnet build` (0 errors), `dotnet test` (204/204, including `TdStoreTests`' renumbering coverage).
Full `*.test.js` suite green, including `td-nav.test.js` and the updated `nav-model.test.js`/
`layout-coverage.test.js`/`server-route-coverage.test.js`/`classic-button-wiring.test.js`/
`split-slots.test.js` coverage for the new `td` page. The disband-while-open reaction was verified
live in the harness: disbanding via a direct `/command` POST while TD sat open flipped it to
"requires an active squad" within the poll window, with no manual refresh or navigation.
