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

## Verification

`dotnet build` (0 errors), `dotnet test` (200/200, including `TdStoreTests`). Full `*.test.js`
suite green, including `td-nav.test.js` and the updated `nav-model.test.js`/
`layout-coverage.test.js`/`server-route-coverage.test.js`/`classic-button-wiring.test.js`/
`split-slots.test.js` coverage for the new `td` page.
