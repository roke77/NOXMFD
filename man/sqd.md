# SQD

Squadron membership over Steam: form a squad with players in your current match, manage the
roster, and see every other squad in the faction.

![SQD page](images/SQD.png)

## Creating a squad

While you have no squad the page opens with a **CREATE SQUAD** row: a callsign picker and a
flight-number row (1-9) — pick both and press CREATE to become the leader. Callsigns are a fixed
list of real military callsigns, not free text. Pending invites don't block it: creating a squad
declines them. **EDIT** on your squad's header lets the leader change both the callsign and the
flight number later — re-numbering the flight immediately updates every member's own designation.

If another squad in the faction already flies the callsign and flight you pick, that flight is
marked amber and a line under the row says so. You can still pick it; the two squads then carry an
amber **SAME DESIGNATION** note.

## Your squad

Your own squad is listed first, in a green box that always stays open. Members render as a table: each pilot's callsign designation, their Steam display name, and their
current aircraft (blank when not flying one). A designation reads `CALLSIGN FLIGHT-MEMBER` — e.g.
`TALON 1-2` — where FLIGHT is the squad's current flight number and MEMBER is the pilot's number
in the squad (the leader is always 1). The leader's row carries an amber LEADER label; on every
other row the leader sees ▲/▼ (move that pilot one number up or down, swapping with whoever has it),
a star (promote) and an × (kick). Your own row is lit green.

Numbers stick. When a pilot leaves, is kicked or drops out, their number stays empty and shows as
an OPEN row; nobody else's number changes. The next pilot to join takes the lowest open number, or
the leader can move someone into it with ▲/▼. When the leader leaves, leadership passes to the
lowest-numbered member (or whoever the leader promoted with the star), who becomes 1, and everyone
else is renumbered from 2 in their current order.

## Other squads

Every other squad in your faction follows, each in its own teal box with its pilot count: click a
header to fold or unfold it. They are read-only. The count at the top right is every squad and pilot
in the faction. You see these squads whether or not you are in a squad yourself, so an ATC
controller with no squad can still read every callsign.

## Designations as in-game names

Every pilot in a squad shows under their designation instead of their Steam name in your game: the
map, kill feed, chat, scoreboard and HUD markers, and every NOXMFD page. That holds for your own
squad, for other squads, and whether or not you are in a squad yourself, as long as the pilot is
running NOXMFD. Leaving a squad brings that pilot's Steam name back. Players without NOXMFD show
under their Steam name. The NOXMFD AKF feed adds the Steam name in parentheses, e.g.
`TALON 1-2 (Roke) [F/A-26]`, and the SQD tables keep both columns.

## Unassigned players

The bar docked at the bottom, `UNASSIGNED PLAYERS (n)`, lists faction-mates in the current match who
are also running NOXMFD and aren't in any squad. Click it to open or close it; it opens upward and
scrolls when long. It is open while you have no squad and collapsed once you lead one. As the leader,
**INVITE** on a row sends an invite, and the player then shows there as INVITED until they answer.
Squad members don't see the list.

## Invites

Incoming invites queue oldest-first under the CREATE SQUAD row and show ACCEPT/DECLINE, with the
designation you would get; accepting one declines the rest, and so does creating a squad, since
squad membership is exclusive. An invite never expires on its own — it stays pending until you
answer it, however long that takes.

## Leaving

**LEAVE** always exits your own squad: immediate as a member; as the leader, it hands off to the
oldest remaining member first, then exits. The star on any other member's row does the same
hand-off-and-exit but lets you pick who takes over instead. **DISBAND** (leader only) ends the
squad for every member at once, rather than just yourself.

If a leader or member crashes or force-quits instead, the rest of the squad notices on its own
within about 30 seconds and drops them, with a notice — no action needed on your end. Pilots
still loading after a mission restart get a longer allowance, so restarting the mission doesn't
break the squad.

## Sharing waypoint routes

Once you're a squad leader with at least one member, each route on the [WPT](wpt.md) page gets a
share button. Sharing pushes the route to every member as a read-only entry with ACCEPT/REJECT;
later edits re-broadcast automatically. A member's own progress through a shared route carries
over across updates. Shared routes unlock for editing the moment the squad ends or the sharer
stops being leader.

## Hand off targets

While in a squad, a **TD** nav item appears on [TGT](tgt.md) — see [TD](td.md) for handing specific
targets off to specific members.
