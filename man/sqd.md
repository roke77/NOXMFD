# SQD

Squadron membership over Steam: form a squad with players in your current match, and manage the
roster.

![SQD page](images/SQD.png)

## Creating a squad

**CREATE SQUAD** swaps in a callsign field — enter a name and confirm to become the leader. **EDIT**
on an existing squad's title lets the leader rename it later.

## Roster

Members render as a table: join order and callsign, each player's Steam display name, and their
current aircraft (blank when not flying one). The leader's row carries a LEADER badge; on every
other row the leader sees a star (promote) and an × (kick). Your own row is highlighted.

**INVITE** picks from faction-mates in the current match who are also running NOXMFD.

## Invites

Incoming invites queue oldest-first and show ACCEPT/REJECT; accepting one declines the rest, since
squad membership is exclusive. An invite times out after 15s with no response.

## Leaving

**LEAVE** always exits your own squad: immediate as a member; as the leader, it hands off to the
oldest remaining member first, then exits. The star on any other member's row does the same
hand-off-and-exit but lets you pick who takes over instead. **DISBAND** (leader only) ends the
squad for every member at once, rather than just yourself.

## Sharing waypoint routes

Once you're a squad leader with at least one member, each route on the [WPT](wpt.md) page gets a
share button. Sharing pushes the route to every member as a read-only entry with ACCEPT/REJECT;
later edits re-broadcast automatically. A member's own progress through a shared route carries
over across updates. Shared routes unlock for editing the moment the squad ends or the sharer
stops being leader.

## Hand off targets

While in a squad, a **TD** nav item appears on [TGT](tgt.md) — see [TD](td.md) for handing specific
targets off to specific members.
