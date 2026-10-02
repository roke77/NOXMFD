# SQD — Squadrons

Fly as a squadron with other NOXMFD pilots in your match. A squadron gives every member a callsign
designation such as `TALON 1-2`, which replaces their Steam name in the game for everyone running
NOXMFD. It also links its members for shared navigation, target hand-offs and squad marks on the
map and HUD. The SQD page is where you form, join, lead and leave a squad, and where you see every
squad flying in your faction.

![SQD page, squad leader](images/SQD.png)

## What a squadron does

- **Callsigns everywhere.** Each member flies under their designation on the game's map, kill feed,
  chat, scoreboard and HUD markers, and on every NOXMFD page. Every NOXMFD player in the faction
  sees these names, including pilots in other squads or in no squad, such as an ATC controller.
- **Shared navigation.** The leader can share routes and steer points from [WPT](wpt.md).
- **Target hand-offs.** The leader assigns targets to specific members from [TD](td.md); members
  answer them on [TGT](tgt.md#squad-designations).
- **Squad marks.** Squadmates' planes show in teal on [MAP](map.md), and the HUD marks units your
  squad has targeted (see [TD → HUD marks](td.md#hud-marks)).

## Before you start

- Every pilot needs NOXMFD, a Steam copy of the game, and to be in the **same match and faction**.
- Squads talk over Steam's own peer-to-peer messaging. There's no server and no account to create.
  On a non-Steam launch the page shows *Squad requires Steam* and the squad feature is off.
- Players without NOXMFD aren't affected: they can't join a squad and keep seeing Steam names.
- A squad lasts until it's left or disbanded. It carries over a mission change or restart, but not
  a game restart: after one, form the squad again.

## The page

Top to bottom:

1. **SQUADRONS**, with how many squads and pilots fly in your faction on the right.
2. **CREATE SQUAD** — only while you're not in a squad.
3. **Invites** you haven't answered — only while you're not in a squad.
4. **Your squad**, in a green box that always stays open.
5. **Every other squad** in your faction, each in a teal box, collapsed. Click a box's header to
   open or close it.
6. **UNASSIGNED PLAYERS**, docked at the bottom — not shown to squad members. It starts open while
   you're not in a squad and collapsed once you are.

The page remembers which boxes you opened or closed, and the unassigned bar, for the rest of the
browser session.

An amber banner above the list reports anything that happened without you pressing a button,
such as a lost squadmate (see [Notices](#notices)).

## Callsigns and numbers

A designation reads `CALLSIGN FLIGHT-NUMBER`, for example `TALON 1-2`:

- **CALLSIGN** is the squad's name, picked from a fixed list of real military callsigns.
- **FLIGHT** is the squad's flight number, 1 to 9.
- **NUMBER** is the pilot's place in the squad. The leader is always `1`.

Numbers stick. When a pilot leaves, is kicked or drops out, their number stays empty as an **OPEN**
row and nobody else is renumbered, so no one's name changes mid-flight. The next pilot to join takes
the lowest open number. When the leader leaves, the new leader becomes `1` and everyone else is
renumbered from `2`, keeping their order.

Two squads may fly the same callsign and flight. Both then show an amber **SAME DESIGNATION** note,
and the pickers mark that pair in amber, but nothing stops you from using it. On comms, use a
different flight number to keep the two apart.

## Start a squad

![SQD page, no squad](images/SQD_NO_SQUAD.png)

While you're not in a squad, the page opens with the **CREATE SQUAD** row:

1. Pick a callsign.
2. Pick a flight number, 1 to 9. A flight another squad already flies under that callsign is marked
   amber, and a line under the row names it.
3. Press **CREATE**. You're the leader, `CALLSIGN FLIGHT-1`.

Creating a squad declines any invites you haven't answered.

## Join a squad

![SQD page, invites pending](images/SQD_INVITED.png)

An invite shows as a card under the CREATE SQUAD row with the squad's callsign and flight, who sent
it, and the designation you'd get, for example `from DeckJockey · as VIPER 2-4`.

- **ACCEPT** joins that squad and declines every other invite, since you can only be in one squad.
- **DECLINE** turns that one invite down.

Invites queue oldest first and never expire: one stays until you answer it. While invites wait you
can still create your own squad, browse the other squads to see who's in them, and accept whichever
you like.

## Lead a squad

As the leader, your squad's header has **EDIT**, **LEAVE** and **DISBAND**. Each member's row has
four controls:

| Control | What it does |
|---|---|
| ▲ / ▼ | Moves the pilot one number up or down, swapping with whoever has it, or into an OPEN number. |
| ★ | Makes that pilot the leader. You leave the squad. |
| × | Removes the pilot from the squad. Their number turns OPEN. |

- **Invite** a pilot from the **UNASSIGNED PLAYERS** bar: open it and press **INVITE** on their row.
  They show as *INVITED · AWAITING RESPONSE* until they answer; an invite can't be withdrawn.
- **EDIT** opens the callsign and flight pickers in the header. **APPLY** renames the squad, and
  every member's designation changes at once; each pilot keeps their number.
- **DISBAND** ends the squad for every member.
- **Share** routes and steer points from [WPT](wpt.md#squad-sharing), and hand targets to members
  from [TD](td.md), which appears on TGT's nav row while you lead a squad.

## Fly as a member

![SQD page, squad member](images/SQD_MEMBER.png)

As a member, you see your squad with your own row lit green and the leader marked **LEADER**. The
header has only **LEAVE**. What the leader shares reaches you on your own pages:

- Shared routes and steer points appear on [WPT](wpt.md#squad-sharing) with ACCEPT/REJECT.
- Targets the leader assigns you pop up on [TGT](tgt.md#squad-designations).

## Leave a squad

- **LEAVE** as a member takes you out at once. Your number turns OPEN for the rest of the squad.
- **LEAVE** as the leader hands the lead to the lowest-numbered member, then takes you out. Use ★ on
  a member's row instead to pick who takes over.
- **DISBAND** (leader only) ends the squad for everyone.

Leaving brings your Steam name back for everyone.

If a squadmate crashes or force-quits, the rest of the squad notices on its own within about 30
seconds and drops them, with a notice. A pilot still loading after a mission restart gets about two
minutes, so restarting a mission doesn't break the squad.

## Other squads

Every other squad in your faction is listed below your own, read-only. Each box starts collapsed
with the squad's callsign, flight and pilot count; open it to see each pilot's designation, Steam
name and aircraft. You see them whether you're in a squad or not, so anyone
coordinating several squads, such as an ATC controller, can read every callsign from one page.

The UNASSIGNED PLAYERS bar lists the faction's NOXMFD pilots who aren't in any squad.

## Names in the game

Every squad member's designation replaces their Steam name in your game: the map, kill feed, chat,
scoreboard, HUD markers, and NOXMFD's own pages, including the ATC extension. That holds for your
own squad and for every other squad.

- Only players running NOXMFD see designations; everyone else sees Steam names.
- A name changes within a couple of seconds of a join, leave, kick, rename or renumber.
- The [AKF](akf.md) feed adds the Steam name in parentheses, e.g. `TALON 1-2 (Roke) [F/A-26]`, and
  the SQD tables show both names.

## Notices

| Notice | Meaning |
|---|---|
| *Your squad was disbanded by the leader.* | The squad ended. |
| *You were removed from the squad by the leader.* | You were kicked. |
| *Lost contact with your squad leader…* | Your leader went silent, probably a crash or disconnect. You're out of the squad. |
| *Lost contact with NAME…* | A member went silent and was dropped. Their number turns OPEN. |
| *NAME is already in LEADER's squad — invitation rejected.* | The pilot you invited is in another squad. |
| *NAME tried to recruit MEMBER, who is already in your squad.* | Another leader invited one of your members. Nothing changes. |
| *Couldn't hand off leadership to NAME…* | The pilot you promoted didn't answer. You're still the leader; try again. |

## Controls

Everything on the page is a click or tap. With SQD as the [SOI](keybinds.md#sensor-of-interest-soi),
the [PAD cursor](keybinds.md#pad-cursor) can press any button, and **Cursor Zoom In/Out** scrolls the
page and the unassigned list.

## Troubleshooting

- **A pilot isn't in UNASSIGNED PLAYERS.** They need NOXMFD running, the same faction, and no squad
  of their own. A pilot who just launched shows up within a few seconds.
- **Someone's name doesn't change.** They need to be in a squad and running NOXMFD. Allow a couple of
  seconds after a squad change.
- **The page says *Squad requires Steam*.** The game didn't start through Steam, so squads are off
  for this session.

## Privacy

Squads exchange data over Steam with other players. Your designation parts, squad leader and fuel
level go to every NOXMFD player in your faction; routes, target hand-offs and squad marks go only to
your own squad. [SECURITY.md](../SECURITY.md) lists exactly what is sent and to whom.
