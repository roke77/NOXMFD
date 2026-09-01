# Squadron transport — sharing data between players over the internet

A transport layer letting two or more players form a "squadron" and exchange mod data directly:
the squadron leader assigns targets, publishes a datalink picture, shares a waypoint route, or
streams a sensor feed, and members receive it on their own displays.

The waypoint route is the smallest consumer of this transport, not its purpose. The transport is
sized for the whole family:

- **Waypoint routes** — the leader shares a planned route (the WPT page's existing JSON).
- **Advanced target designation** — the leader assigns specific targets to named members.
- **Advanced datalink sharing** — a merged contact picture pushed continuously to the squadron.
- **Sensor/video feed sharing** — a member's TGP camera feed mirrored to another member's display.

These four have payloads three orders of magnitude apart, which is the single biggest constraint on
the design. A transport that comfortably carries a route can be useless for video.

**Current scope: small text payloads only** — waypoint routes and target designation. Datalink
streaming and video sharing are deferred; the "Deferred: heavy payloads" section records what they
would need so the first implementation does not foreclose them.

**Shipped:** the transport (Option B, below) plus a full leader/member squad protocol on top of
it — invites, single-squad-per-player enforcement, leader succession, and disband — with waypoint
route sharing and target designation wired end to end as its two payloads (the latter is issue #47,
docs/target-designator.md — its own doc covers the TD-specific command/UI details, not repeated
here). See "Implementation" below for what actually exists in the code today; the sections above
and below it are the design investigation that led there and are kept as the historical record of
why it looks the way it does.

**Confirmed live** across two real machines on separate Steam accounts (a PC host and a Steam Deck
client): squad creation, invite, and a shared waypoint route all reached the other side over Steam's
own P2P networking. The first `SendTo` call(s) in a fresh session commonly return
`k_EResultConnectFailed` while the underlying Steam Networking Messages session is still
negotiating — expected P2P connection-establishment delay, not a bug — and later sends succeed once
it's up.

## What the platform already provides

Findings from the shipped game binaries (`NuclearOption_Data/Managed`) and this mod's own sources.

| Finding | Evidence |
| --- | --- |
| The game networks with **Mirage** (a Mirror fork) over a **Steamworks socket** | `Mirage.dll`, `Mirage.SteamworksSocket.dll` |
| The game already uses **Steam lobbies** for multiplayer | `NuclearOption.Networking.Lobbies.SteamLobby` |
| A full **Steamworks.NET** binding ships with the game | `com.rlabrecque.steamworks.net.dll` |
| Steam's modern P2P messaging API is available | `SendMessageToUser`, `ReceiveMessagesOnChannel`, `AcceptSessionWithUser`, `CloseSessionWithUser` |
| Steam exposes per-message reliability and channels | `k_nSteamNetworkingSend_Reliable`, `_Unreliable`, `_NoDelay`, `_NoNagle`, and a channel argument |
| Steam lobby APIs are available | `CreateLobby`, `SetLobbyData`, `SetLobbyMemberData`, `SendLobbyChatMsg`, `GetNumLobbyMembers` |
| The game does **not** use Steam's lobby-chat channel | no `SendLobbyChatMsg` / `LobbyChatMsg_t` reference in `Assembly-CSharp.dll` |
| The game has **no networked map markers** to reuse | no `Cmd`/`Rpc`/`Target` RPC for pin, marker, or waypoint; `AddFactionPin` is client-local |
| This mod already references `Mirage.dll` | `NOXMFD.csproj` |
| Steam app ID | `2168680` |
| The game **pumps Steam callbacks every frame** | `SteamManager.Update()` calls `SteamAPI.RunCallbacks()` |
| The game initialises Steam and **throws if initialised twice** | `SteamManager.CheckInit()` — `"Tried to Initialize the SteamAPI twice in one session!"` |
| The game's transport uses **`SteamNetworkingSockets`**, not `SteamNetworkingMessages` | `Mirage.SteamworksSocket.dll` references `CreateListenSocketP2P` / `ConnectP2P` only |
| The game registers a **global** `Callback<LobbyCreated_t>` | `SteamLobby` — `Callback<LobbyCreated_t>.Create(OnLobbyCreated)` |

Three of these decide the design:

- **Callbacks are already pumped.** `SteamManager.Update()` runs `SteamAPI.RunCallbacks()` every
  frame, so a mod-registered `Callback<T>` dispatches without the mod running a pump of its own.
  Steam is initialised by the game and throws on a second init, so the mod borrows the session and
  never initialises or shuts down Steam itself.
- **`SteamNetworkingMessages` is completely unused by the game.** The game's transport is the
  distinct `SteamNetworkingSockets` interface. The messaging interface is therefore free for the
  mod to use with no risk of interfering with match traffic.
- **Steam lobbies are not free of interference.** `Callback<T>.Create` in Steamworks.NET is
  process-global, so a lobby created by the mod also fires the game's `OnLobbyCreated`. If the
  game has a lobby creation pending, the mod's result can satisfy the game's completion source and
  corrupt its lobby state. Lobbies are therefore excluded from the first implementation.

Steam is initialised by the game itself (`SteamManager`), in the same process the mod runs in, so
the mod can call the Steamworks API without initialising or shipping anything of its own. Valve's
relay network handles NAT traversal, encryption, and identity at no cost to the project.

The absence of networked map markers matters: there is no native feature to piggyback for any of
the four target features. All of them require the mod to move its own bytes.

## Payload budget

The four features differ enough that they need different channels, not just different message types.

| Feature | Payload | Rate | Bandwidth | Reliability |
| --- | --- | --- | --- | --- |
| Waypoint route | **436 B** measured (10 waypoints, compact JSON) | On demand | Negligible | Reliable, ordered |
| Target designation | ~100–200 B estimated | A few per minute | Negligible | Reliable, ordered |
| Datalink picture | ~2 KB estimated (30 contacts) | 5–10 Hz | 10–20 KB/s | Unreliable — a stale contact set has no value once the next one arrives |
| TGP video feed | ~10–30 KB/frame estimated | 15 Hz | **150–450 KB/s (1.2–3.6 Mbps)** per receiver | Unreliable |

Route and designation payloads are measured and estimated respectively against the existing WPT
JSON. The video figures derive from `TgpFeed.cs`'s shipped encoder settings — 15 Hz, JPEG quality
50, longest side capped at 720 — and need confirming against a real captured frame before any video
work starts.

Video is the constraint that shapes the architecture. A leader streaming to three members sends
3.6–10.8 Mbps upstream, which exceeds the upload capacity of many domestic connections. Options for
keeping it inside a realistic budget, in the order worth trying:

1. Lower the shared feed's rate and quality independently of the local feed (5 Hz, smaller cap,
   quality 35 puts one receiver near 40–80 KB/s).
2. Allow one video receiver at a time rather than fanning out to the whole squadron.
3. Replace per-frame JPEG with a real video codec, which is a large piece of work and pulls in a
   dependency the project does not currently have.

The first two are configuration and policy; the third is a project of its own. Routes, designation,
and datalink all fit comfortably regardless of which is chosen.

## Options

### A. Steam lobby only

The mod creates its own Steam lobby, separate from the match. Membership, invites, and the Steam
overlay's friend UI come free. Data rides `SendLobbyChatMsg` (4 KB binary per message) or
`SetLobbyMemberData` (replicated to members automatically).

Carries routes and target designation without effort. Lobby data and lobby chat are metadata
channels, rate-limited by Steam and not intended for sustained streaming, so datalink at 10 Hz and
video are out of reach.

**Effort:** 2–4 days. **Cost:** none.

### B. Steam peer-to-peer messages

`SteamNetworkingMessages.SendMessageToUser` addressed to each member's SteamID, over Valve's relay.
Messages up to 512 KB, an application-defined channel per message, and a reliability flag per send.
`AcceptSessionWithUser` provides the accept/reject handshake for an unsolicited sender, so consent
is a platform primitive rather than something the mod invents.

Channels map directly onto the payload budget: reliable ordered for routes and designation,
unreliable no-delay for datalink and video. This is the only option that covers all four features.

Membership, invites, and discovery are not included — that work is what Option A provides.

**Effort:** 3–5 days for the transport itself. **Cost:** none.

### C. Custom Mirage message on the live game session

Register a message type with `MessageHandler.RegisterHandler<T>` on the connection the match already
holds. Reuses an established session and adds no new networking.

The blocking problem is that the host or server must also run the mod. An unregistered message ID
charges the sender's error budget and reaches `HandleExceptionInMessage`, which calls
`player.Disconnect()`. Against a vanilla server this risks disconnecting the players using the mod.
The approach is confined to private, fully modded servers, and it only works while everyone is in
the same match — so it cannot support planning before a flight.

**Effort:** ~1 week, fragile. **Cost:** none.

### D. External relay server

A WebSocket service with room codes, connected to by either the browser UI or the plugin. Platform
independent, so it would also serve non-Steam copies, and a relay topology solves the video fanout
problem by sending upstream once.

It makes the project an operator: uptime, abuse handling, a privacy policy, and a running bill that
scales with the number of users. It also replaces a Valve-operated relay with a self-operated one
for no functional gain on Steam.

**Effort:** ~1 week, plus ongoing operations. **Cost:** $0–5/month at hobby scale, rising with use.

### E. Manual export and import

The status quo. The WPT page already exports and imports route JSON, so a route can be shared by
pasting it into a chat client.

Covers exactly one of the four features, and only for pre-flight planning. Listed because it is the
baseline any transport has to justify itself against.

**Effort:** none, already shipped. **Cost:** none.

## Recommendation

**Option B alone for the first implementation. Option A is deferred, not adopted.**

`SteamNetworkingMessages` carries everything the current scope needs: `SendMessageToUser` addressed
to a member's SteamID, reliable and ordered, on a mod-chosen channel the game never touches.
`AcceptSessionWithUser` supplies the consent handshake, and `CloseSessionWithUser` the teardown.
Steam's relay handles NAT traversal, encryption, and identity at no cost.

The Steam lobby was the earlier recommendation for the membership layer, and the `LobbyCreated_t`
finding above removes it from the first pass: a mod-created lobby can corrupt the game's own lobby
state through a process-global callback. Membership for small text payloads does not need a lobby —
a squadron is a set of SteamIDs, and messages address those directly. A lobby buys invite UX and
discovery, which can be revisited once the transport is proven and the callback interaction has
been tested against a live match.

This costs nothing, requires no server, needs no account beyond Steam, and leaves vanilla players
unaffected.

Option C is rejected for public play because it can disconnect the players using it. Option D
remains the fallback if the project ever needs to serve non-Steam copies, or if video fanout proves
impossible peer-to-peer.

Encoding payloads into the game's own chat is rejected outright. `ChatManager` caps a message at
`MAX_CHAT_MESSAGE_LENGTH = 128` and applies `[RateLimit(Refill = 5, MaxTokens = 15, Penalty = 1,
Interval = 30f)]` server-side. The measured 436-byte route becomes 584 base64 characters, or five
messages — an entire refill period for the smallest of the four payloads, spamming a channel other
players are reading, with every message logged server-side against the sender's SteamID.

## Architecture sketch (as originally proposed)

The plan below predates the plugin owning route data (`RouteStore`, `docs/hud-waypoint-indicator.md`)
and predates the leader/member protocol — kept for context on the browser/plugin split it got right.
See "Implementation" for what's actually built.

The browser holds the data (routes in `localStorage`), and the plugin holds the network connection,
so payloads cross that boundary in both directions. Both directions already exist:

- **Browser to plugin:** `POST /command`, dispatched by `CommandDispatcher`. Squadron sends become
  new commands carrying a payload string.
- **Plugin to browser:** the SSE stream. The `cursor` event shows the pattern for a dedicated event
  type alongside the telemetry frame, which is what a squadron event wants — payloads arrive
  independently of the 10 Hz tick and should not wait for it.

That leaves the genuinely new work as: the Steam lobby lifecycle, the P2P send/receive loop pumped
from the existing Unity update, a message envelope with a version field, the squadron UI, and the
consent model.

Two design constraints worth fixing early:

- **Version the envelope from the first message.** Squadron members will run different mod versions,
  and a transport carrying four independently evolving features needs to reject or ignore what it
  cannot parse rather than misread it.
- **Treat every received payload as untrusted input.** It arrives from another player's machine over
  the internet. Bounds and schema belong at that boundary, as with any other trust boundary in the
  mod.

## Implementation

What actually shipped, layered on top of Option B:

**Transport (`Squadron.cs`)** — a generic per-peer Steam messaging layer: `SendTo`/`SendToAll`,
`Poll`/`Since` for the inbox, a versioned envelope (`{v,type,payload}`), and session bookkeeping
(`OpenSession`/`CloseSession`). It accepts a session from **anyone**, not just a pre-approved list —
an invite has to reach a stranger before they can decide whether to join, so the earlier "only
accept known peers" gate doesn't fit a leader/member model. Consent moved entirely to the protocol
layer below: each message type decides for itself whether a given sender is trusted to send it
(e.g. an `sqd.invite` is welcome from anyone; an `sqd.roster` is only trusted from the current
leader).

**Protocol (`Squad.cs`)** — the leader/member state machine, star-topology (leader holds a session
with every member; members only ever talk to the leader, never each other):

- A squad comes into being via `Squad.CreateSquad(callsign, flight)` (SQD page's CREATE SQUAD
  button) — leader-only from that point on, requiring both a callsign and a flight number (1-9) up
  front rather than leaving the squad unnamed; there is no implicit creation via a first invite
  anymore. `Invite` (and therefore the roster's own INVITE button) is only reachable once
  `CreateSquad` has already made this pilot a leader. Callsign and flight are the Squadron Callsign
  System (issue #42) — see "Squadron Callsign System" below.
- `sqd.invite` / `sqd.accept` / `sqd.decline` — explicit accept required, no auto-join. Incoming
  invites queue (oldest first) rather than the newest replacing an undecided one — a pilot can have
  several outstanding at once, each independently accept/decline-able by its sender's SteamID;
  accepting any one declines the rest automatically, since joining a squad is exclusive.
- Single squad per player, enforced socially (no server to arbitrate): an invite target already in
  a squad sends back `sqd.conflict` (rejecting it) and, if they're a member, `sqd.poach` to their
  *real* leader — a warning that someone tried to recruit one of their people. The same exclusivity
  runs the other way too — `CreateSquad`/`Invite` both refuse to act while this pilot's own incoming
  invite(s) are still undecided.
- `sqd.roster` — the leader broadcasts the full member list on every change; members never need
  their own source of truth for who's in the squad.
- `sqd.leave` (member) / `sqd.transfer` + `sqd.leader-changed` (leader handing off, explicit pick or
  auto-picked oldest-joined member) / `sqd.disband` (leader only) / `sqd.kick` (leader removes one
  member while the squad lives on — distinct from disband; the target gets its own `sqd.kick`
  message rather than just falling out of the next roster broadcast, since by the time that goes
  out they're no longer in `_members` to notice themselves missing). None of these proactively close
  the sender's own session with the recipient anymore — Steam can drop an already-queued reliable
  message if the session closes before it flushes, so the goodbye itself could go missing right
  when it matters most. Each receiving handler closes its own end once it actually gets the message.
- `Squad.CheckLiveness()` — no protocol message at all, unlike everything above: a crash or
  force-quit gives no chance to send one, so this instead reuses Presence's existing per-peer TTL
  (the same 15s window `PlayerRoster`'s invite-candidate filter already trusts) to notice a
  leader/member who's gone silent and clean them out locally. Runs once a second, alongside
  `PlayerRoster.Refresh()`/`Presence.Tick()`.
- `sqd.set-callsign` — leader-only, renames an existing squadron AND re-numbers its flight (issue
  #47 follow-up added the flight half; the initial values both come from `CreateSquad` above);
  carried through every roster/invite envelope and a leadership handoff (`sqd.transfer`'s own
  envelope) so both survive. SQD's page title reads "`<CALLSIGN> SQUAD`" and doubles as the inline
  editor (EDIT swaps the title for a callsign+flight picker in place). Re-numbering the flight
  re-numbers every member's own `"<CALLSIGN> <FLIGHT>-<MEMBER>"` designation immediately, since
  MEMBER (join order) never changes.
- `sqd.data` — the generic data slot this doc's scope section describes. WPT uses it for per-route
  and per-steer-point sharing; share buttons only show for a squad leader with at least one member.
  `Squad.SendDataTo` is the per-recipient sibling
  `td.designate` (issue #47, docs/target-designator.md) needs, since different members get
  different target sets rather than one broadcast payload. The route flow (`wpt.route` and
  `wpt.route-deleted`) adds the following on top of the bare transport (`RouteStore.cs`):
  - **Accept/reject.** An incoming share lands as a `PendingSharedRoute` (id/name/waypoints from
    the leader, plus who sent it), never persisted to disk — it only makes sense within the live
    squad session. WPT shows it as its own row with ACCEPT/REJECT, nothing else; accepting adds a
    real, read-only route (`Route.SharedBy` non-empty gates every mutator).
  - **Dedup, not just accept-once.** A repeat share of an id already pending just refreshes that
    pending entry in place; a repeat share of an id already accepted updates that route's content
    (`RouteStore.UpdateSharedRoute`), rather than either piling up duplicates or being silently
    dropped.
  - **Progress-preserving updates.** When the leader edits and re-shares, the member's own
    `NextIndex` follows the same logical next waypoint to its new position (matched by name/x/z —
    a re-shared waypoint carries no stable id) instead of resetting to zero; if that exact waypoint
    was deleted, progress advances to just past whichever already-completed waypoints survive.
  - **Auto-reshare.** Clicking share once sets `Route.SharedWithSquad`; every later edit to that
    route re-broadcasts on its own from then on, no repeat click needed.
  - **Delete tombstone.** Deleting a route that was ever shared (`Route.SharedWithSquad`) sends a
    `wpt.route-deleted` payload — just the route id — so members drop their pending or accepted
    copy instead of keeping a stale one forever with no way to learn it's gone.
  - **Unlocks when the squad ends.** `RouteStore.OnSquadEnded()` — called from inside `Squad.cs`'s
    `ResetToNone()` (the one place every squad-ending path funnels through: `Leave`, `Disband`,
    `RelinquishLeadership`, `HandleDisband`, `HandleKick` — centralized there after a squad/TD
    lifecycle audit found it missing from several of these), plus separately from
    `HandleLeaderChanged` and `HandleTransfer` (one rule, not one per event: `SendData` is
    leader-only, so a former leader is cut off from ever pushing another update the moment they stop
    being leader, whether the squad itself lives on or not — `HandleTransfer`'s successor is the one
    member who doesn't go through `HandleLeaderChanged`, so it needs the same call on its own path)
    — clears any still-pending shares and clears `SharedBy` on every accepted route, unlocking it
    for editing.
  Steer points mirror that lifecycle through `wpt.steerpoint` and `wpt.steerpoint-deleted`: each
  point is shared independently, remains pending until accepted/rejected, is read-only after
  acceptance, updates in place on repeat sends, auto-reshare after its first share, and is removed
  by an id-only tombstone. Squad teardown clears pending points and unlocks accepted ones. See
  `docs/steer-points.md` for navigation semantics and the portable collection format.
- Sent invites never expire — they live until the target accepts or declines, however long that
  takes. There is no delivery acknowledgment at the Steam messaging level, so a target with no mod
  installed looks identical to one still deciding, and a timeout couldn't tell the two apart
  anyway. An accept against a `_pendingSent` entry the leader no longer has (they disbanded,
  restarted, or already invited someone else into that slot) is a silent no-op on the leader's
  side — `HandleAccept` only acts on a `from` it still recognizes; there's no error to surface.
- No persistence, by design: squad state is in-memory only and resets on plugin restart.

**Membership picker (`PlayerRoster.cs`)** — no pasted SteamIDs. The leader picks from everyone
currently in their own faction for the match, read via `FactionHQ.GetPlayers()` (the same source
the game's own scoreboard uses) — any client can read this, not just the host, since `Player` is an
ordinary spawned Mirage object with a synced SteamID. Scoped to one faction: a squad only makes
sense among teammates.

The same `FactionHQ.GetPlayers()` scan also builds a SteamID → current-aircraft-unitName lookup
(`PlayerRoster.AircraftFor`), unrelated to the invite-candidate filtering above and unfiltered by
Presence — it's a plain `Player.Aircraft` SyncVar read (null → "", the same as anyone not found at
all), nothing to do with whether its owner is running NOXMFD. This is what lets SQD show every
squad member's current plane without any new P2P message: the game already replicates it to every
client in the faction, peer-to-peer relay was never needed for this specific piece of data.

The same scan also keys that lookup by the aircraft's `persistentID` instead of just its type name
(issue #48, docs/map-squad-styling.md) — purely a local visual, not a transport payload: MAP tints
a squadmate's icon teal instead of its plain faction color, using nothing this doc's P2P messaging
carries.

**UI** — a new SQD page (`src/web/pages/sqd/`), reachable from MAIN in both layouts, replacing the
squadron block that used to live on WPT. The roster renders as a table, not plain rows: first
column is each pilot's Squadron Callsign System designation (see below), second is the player's
Steam display name, third is their current aircraft (icon reused from `/icon?type=`, the same
endpoint MAP draws its blips from — blank, not a placeholder, whenever `AircraftFor` has nothing to
report), and a trailing LEADER badge or, on a subordinate's row when viewing as leader, a star
(promote, `sqd.relinquish`) and an × (kick, `sqd.kick`). The pilot's own row highlights in place of
the old "highlight the leader" behaviour, so a member can find themselves in their own squad at a
glance.

While there's no squad yet, a centered CREATE SQUAD button (swapping in place for the callsign and
flight-number pickers + CREATE/CANCEL, same idiom as the roster's own EDIT) is what starts one —
there's no unnamed-squad state to render, since `CreateSquad` requires both up front. Every section
on the page (current squad, incoming invites, the match roster) shows or hides independently off
current state rather than gating each other: an incoming invite no longer hides the roster below
it, and the roster itself stays visible and browsable even while undecided — only its own INVITE
button (and CREATE SQUAD) actually needs `role`/pending-invite state, so those are disabled with an
explanatory tooltip rather than the whole section disappearing.

## Squadron Callsign System (issue #42)

Both the callsign and the per-member numbering follow a real military callsign convention instead
of free text and a bare join-order count.

- **Callsign** — CREATE SQUAD's callsign field is a fixed `<select>`
  (`src/web/pages/sqd/callsigns.js`), not a text input: a deduped, alphabetized list flattened from
  a real DCS World callsigns reference across every aircraft/role category (GitHub issue #42's own
  comment) — this list only cares about the name itself, not which aircraft type it was originally
  associated with. `sqd.set-callsign` (EDIT) uses the same picker later.
- **Flight number** — a second `<select>`, 1-9, chosen at CREATE SQUAD time
  (`Squad.CreateSquad(callsign, flight)`) and editable later via the same EDIT picker as the
  callsign (`Squad.SetCallsign(name, flight)`, issue #47 follow-up).
- **Per-member designation** — every roster row (SQD) and squad button (TD, docs/target-designator.md)
  renders `"<CALLSIGN> <FLIGHT>-<MEMBER>"`, e.g. `TALON 1-2`: `FLIGHT` is the squad's current flight
  number, `MEMBER` is the pre-existing join-order number (1 = leader, `Squad.cs`'s own `_members`
  list only ever appends so index order IS join order — unchanged by this feature, just given a new
  display format). Matches the standard "Flight Lead / Wingman / Element Lead / Element Wingman"
  4-ship structure (`CALLSIGN FLIGHT-AIRCRAFT` format) a real squadron uses.

## Security and privacy consequences

**`SECURITY.md` has not been updated yet** — still pending, tracked separately from this doc.

`SECURITY.md` currently states, as a headline promise, that the mod **makes no internet
connections** and that all traffic is localhost or LAN. Every option here except E makes that
sentence untrue.

Adopting A+B requires that disclosure to change honestly, and the change is defensible: connections
are opt-in, only to Steam users the player invites or accepts, over Valve's relay, with no
third-party server and no data collected by the project. That is a materially stronger position
than Option D, which would introduce a server operated by the project through which player data
passes.

The disclosure needs to cover what is actually sent — position, sensor, and targeting data, and
potentially a camera feed — since a squadron feature shares live information about the player's
aircraft with other people by design. It also needs to state that accepting a squadron invite
reveals the player's SteamID and, through Steam's relay, exposes them to another player's traffic.

The consent model should be explicit rather than implicit: no automatic acceptance of sessions, a
visible member list, and a way to leave that closes sessions (`CloseSessionWithUser`).

## Deferred: heavy payloads

The datalink and video features are out of the current scope. They are recorded here so the
transport built now does not foreclose them.

**Datalink streaming** needs an unreliable channel — a contact set that arrives late has been
superseded, so retransmitting it costs latency and buys nothing.
`k_nSteamNetworkingSend_UnreliableNoDelay` on a channel separate from the reliable one covers this,
which is why the envelope carries a type field and the send path takes a channel from the start.
The open design question is whether the picture merges on the leader's machine or on each member's.

**Video sharing** is the one feature capable of changing the architecture. At the shipped TGP
settings it needs an estimated 1.2–3.6 Mbps *per receiver*, so a leader feeding three members
exceeds many domestic upload links. Before any video work, measure a real encoded frame — every
figure here rests on an estimate — then decide between a reduced shared-feed rate, a single
receiver at a time, or a real codec. A relay topology (Option D) becomes attractive only if
peer-to-peer fanout proves unworkable.

Both are additive: they need new channels and new message types, not a different transport.

## Open questions

The two unknowns capable of invalidating the recommendation were answered statically before any
code was written — callbacks are pumped by `SteamManager.Update()`, and `SteamNetworkingMessages`
is untouched by the game. Of the rest:

- ~~Does a squadron survive leaving a match, or dissolve with it?~~ **Answered:** squad state is
  static/in-memory, not mission-scoped, so it survives a mission change and only ends on an
  explicit leave/disband (or a plugin restart, since there's no persistence).
- ~~How do members identify each other without a lobby — a pasted SteamID, or a picker?~~
  **Answered:** a picker, sourced from `FactionHQ.GetPlayers()` — the leader's own faction roster
  for the current match, not a pasted SteamID.
- ~~Does an unmodded or non-Steam copy need a graceful "unavailable" path?~~ **Answered:** yes, and
  built — the SQD page's whole squad section hides when `Squadron.Ready` is false.
- **Still open:** what is a real TGP frame's encoded size? Deferred with the video feature, but it
  gates any decision there.
- ~~Inviting a player with no mod installed is silently indistinguishable from inviting one who's
  still deciding — there's no delivery acknowledgment at the Steam messaging level.~~ **Answered:**
  `Presence.cs` — every NOXMFD instance broadcasts a lightweight "presence" beacon to its whole
  faction roster every 5s over this same transport (its own message type, its own drain cursor into
  `Squadron`'s shared inbox) and tracks a per-peer last-seen time with a 15s TTL. `PlayerRoster.cs`
  filters `/server-players` to only peers currently within that TTL, so SQD's invite roster only
  ever offers players who could actually receive and answer an invite — the common case (no mod
  installed at all) no longer produces a dead-end invite in the first place. Invites themselves
  have no timeout of their own (see above), so a genuinely-present target can take as long as they
  want to decide.
