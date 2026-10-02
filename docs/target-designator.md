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

Every TD command reuses an existing `CommandEnvelope` field — none needs a new one:

| Command | Fields reused | Effect |
| --- | --- | --- |
| `td.select` | `id` | Leader: toggle a row's selection. |
| `td.assign` | `index` (slot), `on` (retain) | Leader: toggle every selected target's membership in that slot, then clear selection unless `on` (a long-press — see below). |
| `td.assign-all` | `on` (retain) | Leader: the `<CALLSIGN> ALL` button — every squad slot (`Squad.AllSlots()`, leader included) at once via `TdStore.AssignAll`. All-or-nothing rather than a per-slot toggle: if every selected target already has every slot it removes them all, otherwise it adds them all, so a partly-assigned target gains the missing slots instead of losing the ones it had. |
| `td.clear` | — | Leader: wipe selection + assignments. |
| `td.designate` | `peer`, `text` | Leader: `Squad.SendDataTo(peer, "td.designate", text)` — one call per member with 1+ assigned targets. |
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

## DESIGNATE stays on TD, and TGT shows the result

Two small pieces close the loop with TGT (issue #47 follow-up):

- **DESIGNATE (leader) stays on TD.** It sends and the columns turn SENT; the leader chooses when to go back
  to TGT. (It used to return to TGT through a `td-designated` shell message; that path is removed.)
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
`SendToAll`. On the receiving end, `Squad.HandleData` applies a `td.designate` payload directly —
`TdStore.ReceiveDesignation(payload)` — the instant the Steam message arrives, on the same
main-thread `Drain()` call that received it; no browser round trip is involved. `TdStore.StateJson`
changing is what the member actually sees: `SseHub.cs`'s per-connection loop change-gates on it and
pushes an `event: td-state`, which every connected display picks up as `'td-state-push'`
(`telemetry-source.js` → shell → `td.js`) and applies straight away. See
docs/sse-push-refactor.md and the "silently sending nothing" section below for why applying this in
the plugin (rather than deferring it to whichever browser tab happened to be open) matters.

## Members are keyed by SteamID

Squads don't number their members (issue #107, [self-callsign.md](self-callsign.md)), so the
matrix keys everything on the member's SteamID: `TdStore` holds target id → member SteamIDs and
member SteamID → the ids last sent. SteamIDs travel as strings in the served state and in
`td.cell {id, peer}`, `td.column {peer, text}` and `td.designate {peer, text}`, since a 17-digit id
overflows a JavaScript number. Columns run leader first, then members in the order they joined, and
are labeled with each pilot's own callsign (`state.pilots`). The leader's own column is tag-only, per
the issue's own scope — DESIGNATE never sends to yourself. A member who leaves drops out of every
assignment (`TdStore.ClearMember`), so nothing they were given follows whoever joins next.

## Keybinds

`TD Keybinds` — `td-assign-1`..`td-assign-9`, registered `edge:false` (no-op held binds) and driven
per-frame by `Keybinds.PollTapHold`, the same `KeybindTapHold` state machine the combat-mode A/A/A/G
binds already use — a plain edge-triggered bind can't distinguish tap from hold, and the on-screen
squad button's own tap-vs-hold gesture (tap assigns and clears the selection, hold assigns and
keeps it) is exactly what these mirror: a tap calls `TdStore.Assign(slot)`, a hold calls
`TdStore.Assign(slot, retain: true)` (both gated on `Squad.IsLeader`), plus a
`TelemetryServer.MapAction("td-assign-N")` broadcast either way for whichever display holds SOI.
Only meaningful on the leader's own TD view while it holds SOI — a member's TD view has no squad
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

- **Assignments to a departed member.** `TdStore._assignments` is keyed by target id -> a set of
  member SteamIDs. A kick, leave or dropout calls `TdStore.ClearMember(id)` from `Squad.cs`'s
  `CleanupRemovedMember`, which drops that member from every assignment (removing the target
  entirely if that was its only member) along with what they were last sent.
- **Reacting to a disband while TD is already open.** TD deliberately has no polling of its own
  (see "A static table, on purpose" above) — a squad ending while the page sits open had no way to
  reach it. First fixed with a one-shot `td-squad-ended` window event, piggybacked on `td-nav.js`'s
  then-existing 2s `/squad` poll. **Superseded** by docs/sse-push-refactor.md: `td.js` now listens
  for the SSE-pushed `'sqd-state'` message directly (same push every squad-aware page rides), so
  this reaches an open TD page at least as fast and without a poll of any kind, and the
  `td-squad-ended` event/nudge was removed as redundant.
- **Reacting to a fresh designation while TD is already open.** The same gap existed the other
  direction — nothing told an open member view that `/td-state` had anything new. First fixed with a
  `td-designation-received` nudge posted right after the plugin applied the incoming payload.
  **Superseded** by the same push: `td.js` listens for `'td-state-push'` directly, which also fixes
  a deeper problem the nudge itself had — it could race the plugin's own command queue and land
  before a designation was actually applied. See docs/sse-push-refactor.md.
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

## Rework: assignment matrix and the TGT dock (feature/target-designation-rework)

Player feedback: TD "is still a bit janky sometimes and the operation isn't intuitive." Two causes
stood out. The leader's flow had a hidden selection step (tap rows, then a squad button, with a
tap-vs-hold rule deciding whether the selection survived). And a member had to open their own TD
page, review, and press AQUIRE before anything reached their cockpit. The designs chosen (TD D ·
Direct Matrix for the leader, TGT C · Dock for the member) live in
`_scratch/claude_designs/TD D direct matrix.*`.

**Leader: a matrix with no selection step.** Targets down the side, squad members across the top.
A cell toggles one target/member pair; a target's name toggles that row across every member; a
column head toggles that member across every target on the table. Row and column are all-or-nothing (fill
the gaps, or empty when full), the same rule the old `<CALLSIGN> ALL` button had. Wire commands:
`td.cell {id, peer}`, `td.row {id}` (members from `Squad.AllMemberIds()`), `td.column {peer, text}`
(`text` = the table's id list as JSON, since the table lives in the browser). `td.select`,
`td.assign` and `td.assign-all` are gone, along with the selection overlay, SELECT ALL, and the
long-press "keep selection" rule.

**Per-member send status.** `TdStore` keeps what each member was last sent (`sent`, recorded by
`TdStore.MarkSent` after a successful `td.designate`). The page compares it with the current matrix
(`td-matrix.js`'s `memberStatus`): SENT, CHANGED, UNSENT, EMPTY, or MARKER for the leader's own
column. DESIGNATE sends only the waiting (CHANGED/UNSENT) members, including an emptied list, which
withdraws that member's pending designation. CLEAR keeps `sent`, so cleared columns read CHANGED
until the next DESIGNATE.

**Leader only.** `td-nav.js` adds the TD nav item only for `role === 'leader'`; the page itself
shows a notice to anyone else. The member view (table, AQUIRE, member REFRESH/CLEAR) is removed.

**Member: the TGT dock.** A pending designation (`TdStore.designated`) shows as an amber bar
absolutely positioned at the bottom of `.tgt-list`, layered over rows, sticky header and scrollbar
(`z-index`), until answered. While it's up, `.tgt-list-rows` gets bottom padding equal to the bar,
so every row can still scroll clear of it. The label opens a drawer of names above the bar;
targets already selected are dimmed LISTED, and ADD's count excludes them. `td.accept {on}` selects
the designated units in-game through `TrySelectTarget` (so TGT filters still apply), after a
`DeselectAll` when `on` (REPLACE); then `TdStore.AcceptDesignated` clears the pending list and
remembers its ids in `accepted`, which TGT uses to tag those rows TD. `td.dismiss` clears the
pending list with nothing selected. `td.acquire-all` and `td.member-clear` are gone.

**Keybinds.** The 9 `td-assign-N` binds assigned the old selection, which no longer exists; they
were removed (Keybinds.cs, the remote-keybind map, the harness parser's self-check). An existing
`.cfg` keeps its orphaned `[TD Keybinds]` entries, harmlessly.

**Leader's TD column on TGT.** A row tap assigns every member at once, which overflowed the old
column width; the column is wider, lists the assigned members' callsigns in squad order, and clips
with an ellipsis (the full list is its tooltip).

### Verification

`tools/ci-check.ps1` green: Release build, all `*.test.js` (new `td-matrix.test.js`), 428 C# tests
(`TdStoreTests` rewritten for the matrix, sent-status and accept paths). In the `serve_web.py`
harness, whose TD mock now loops a DESIGNATE back to the same browser: cell/row/column taps match the
mock's state exactly, statuses move UNSENT → SENT → CHANGED, the TGT dock appears with the right
ADD count and LISTED marks, the drawer and bar sit over a scrolling 17-row list, and ADD closes the
dock and tags the accepted rows. Rendering the manual screenshots as a member caught a class
clash: the dock's label reused `.tgt-td-head`, TGT's existing leader-only TD column header class, so
it was hidden for everyone but the leader; the dock's label is `.tgt-td-label` now.

Still needs an in-game check with two players:

- A member receives a DESIGNATE and the dock appears on their TGT without opening anything.
- ADD selects only the new targets; REPLACE deselects everything first; DISMISS selects nothing.
- A designated target the member's TGT filters exclude is skipped by ADD/REPLACE.
- A second DESIGNATE while the dock is up replaces it; an emptied list withdraws it.
- Column status after a member leaves (their column goes, the others keep CHANGED/SENT).
- The dock and matrix at in-game MFD sizes, including split panes and the F-35 layout.
- The manual screenshots (`TD_SQD_LEADER.png`, `TGT_TD_DOCK.png`, `TGT_TD_DOCK_OPEN.png`) are the real
  pages rendered in the harness with sample data, framed in the classic bezel — retake them in-game
  if they drift from what a live squad shows.
