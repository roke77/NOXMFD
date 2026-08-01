# Extended Keybinds page (/keybinds)

An MFD page for binding the mod's *extended keybinds*: cockpit functions the game has no native
keybind for. It is the only keybind UI — the F1 (ConfigurationManager) menu shows none of these
entries, though the values still persist in the plugin `.cfg`.

Reached from **KEY** on MAIN in both layouts. It is a frame-hosted page exactly like HUD — self
-driven (it polls `/keybinds-config` and POSTs its own `keybind.*` commands, so the shell forwards
it nothing), and reached the same way: `FRAME_PAGES`/`PAGE_URL`/`SPLIT_SLOTS` in the bezel,
`F35_PAGES` in the F-35, with a MAIN back-key from `NAV.keys` in both. So it renders in the classic
`#page-frame` (and as a split pane), and in an F-35 portal, without leaving the shell. `KEY` lives
in `BEZEL_EXTRAS.main` / the F-35's `MAIN_EXTRAS` since `NAV`'s six items don't cover it.

Opening `/keybinds` directly (typed, bookmarked) still serves the same page standalone; there it
shows a `< MAIN` link back to `/` (the sticky-layout head guard resolves that to whichever shell is
current). Embedded in a shell the link is redundant with the shell's own MAIN key, so `keybinds.js`
removes it when `window.parent !== window`.

One table, one row per function, grouped under three section headers — COUNTERMEASURES,
GEAR, WEAPONS. A section can carry a note under its header for behaviour shared by its
binds (WEAPONS uses it for how the cycle keys work), keeping the per-row text short.

| column | content |
|---|---|
| **Function** | the function name with a one-line description beneath it |
| **Keyboard** | current key; click to capture, `Esc` cancels, `×` clears |
| **Joystick / HOTAS** | current button as `J<stick> B<button>`; click to arm capture, `×` clears |

## The binds

| Function | Behaviour | Driven |
|---|---|---|
| Flares | select + deploy IR flares; hold to keep popping | held |
| Jammer | select + activate the radar jammer; HOLD to jam | held |
| Jamming Pod | select + activate a weapon-mounted ECM pod (e.g. the Medusa's); HOLD to keep jamming — see below | held |
| Gear Up / Gear Down | dedicated raise/lower (stock bind is a toggle); no-op if already there, mid-transition, or on the ground | edge |
| Cycle Guns / Missiles / Bombs | select within the class — see below | edge |
| Gun Trigger / Weapon Release | two-stage class fire keys — see below | held |

### Weapon selectors (WeaponSelectors.cs)

Alongside the game's single active-weapon selection, the mod remembers a **gun** choice, a
**missile-or-bomb** choice, and a **jamming pod** choice ("soft selections", pointing at the
same aggregated loadout entries the WPN page lists). Classification uses the game's public
`WeaponInfo` flags: `gun`; `bomb`/`glideBomb`; missiles are the `missile` flag plus flagless
launched ordnance (rockets carry no flag); `jammer` is a weapon-mounted ECM pod (e.g. the
Medusa's Radar Jamming Pod — distinct from the airframe `RadarJammer` countermeasure behind
the "Jammer" keybind above); excluding cargo/troops/sling. The jamming pod has no cycle key —
same two-stage switch-then-fire model as Gun Trigger/Weapon Release, just one soft selection
and no need to advance through a list.

- **Cycle keys select.** With the active weapon in the key's class, each press advances to
  the next entry and makes it active; from another class, the first press recalls the
  class's remembered weapon (or its first) and activates it, and the next press cycles.
  Cycling skips depleted entries; a fully depleted class makes the key a no-op.
- **Fire keys are two-stage across classes.** When the active weapon isn't the key's class,
  a press only *switches* to the class's weapon — bringing up the right reticle — and that
  same hold never fires; release and press again to fire. In-class, hold to keep firing
  (`WeaponManager.Fire()` per frame; the game's `Ready()` rate-limits, so it feels like the
  stock trigger, guns-linked included).
- **Selectors follow the pilot.** Selecting a weapon by any means (stock cycle, WPN page
  tap) snaps the matching selector to it, so the fire keys always commit the most recent
  choice. Stale names after an aircraft change fall back to the first of the class.
- **WPN page display:** both soft selections show as a stroked outline around the entry
  label (red when empty), suppressed when the entry is the actively selected weapon — the
  filled box already says it. A selection change also pages the WPN view (bezel and F-35)
  to the page holding it.

## Capture

Capture is split by source, because each side can only see its own input:

- **Keyboard is captured in the browser** — while you're on the page, keyboard focus is on
  the browser and the game never sees the key. The `KeyboardEvent.code` maps to a Unity
  `KeyCode` name (letters/digits/F-keys/numpad mechanically, the rest via a small table in
  `keybinds.js`); unmappable keys flash UNSUPPORTED. Mouse buttons are not capturable — a
  click is how the page is driven.
- **Joystick is captured by the plugin** (`Keybinds.ArmJoyCapture`) — only Rewired's button
  numbering matches playback (a browser Gamepad index doesn't line up; XInput, for one, is
  offset). While armed, the plugin overrides `Application.runInBackground` and Rewired's
  `ignoreInputWhenAppNotInFocus` so the stick stays live with the browser focused, and
  restores both on disarm. Buttons already held at arm time are excluded — a latched
  toggle switch (VPC mode selectors etc.) would otherwise be "captured" instantly; flip it
  off and on again while armed to bind it deliberately. Each bind pins its own joystick
  number (`0` = any), so a multi-device HOTAS can spread binds across sticks.

## Plumbing

- `GET /keybinds-config` — the bind registry (id, section title, label, description,
  current key + joy button/stick), the per-section `notes`, and which bind is armed for
  capture. The page polls it at 600 ms; the poll is also how a capture result arrives.
  Section display titles and notes come from `Keybinds.SectionTitle`/`SectionNote` — the
  `.cfg` section names underneath are persistence identity and never change.
- `POST /command`: `keybind.set-key { bind, key }` (`""`/`"None"` clears),
  `keybind.arm-joy { bind }`, `keybind.cancel-joy`, `keybind.clear-joy { bind }`. Commands
  drain on the main thread from `MissionLifecycle.Update` (persistent), so the page works
  at the main menu too.
- The registry lives in `Keybinds.cs`: one `BindDef` row per function (config entries,
  edge/held mode, drive action). Adding a keybind is one `Def()` call — the page, the
  JSON, and the polling all pick it up from the registry.
- `tools/serve_web.py` carries a stateful mock of the endpoint and commands (including a
  simulated stick capture), so the whole page is drivable in the harness without the game.

---

# SOI (sensor of interest)

**Status: working on the `soi` branch, classic layout.** All five binds, the focus ring and the
cursor are in. Not done: focus is per display rather than per pane, pages whose controls live
inside their iframe can be reached but not operated, and the F-35 has no cursor of its own.

Borrowed from DCS: one display at a time is the *sensor of interest*, and a fixed set of
HOTAS keys drives whichever display that is. Here a "display" is one MFD instance — a
browser somewhere on the network — and, within it, one pane. Focus moves; the keys don't.
The point is to work a screen you are not touching: a tablet clamped to the rig, a second
monitor, a phone velcroed to the throttle.

Five binds, in the page's own SOI section:

| Bind | Effect |
|---|---|
| `NAV UP` / `NAV DOWN` | move a cursor through the focused display's line-select keys |
| `SELECT` | press the cursored key — the same thing clicking it does |
| `SOI NEXT` / `SOI PREV` | move focus to the next/previous display, wrapping |

They are the only binds that act on the mod rather than on the aeroplane, which is why they are
registered with `DefFree` and run in `Poll()`'s aircraft-free pass — every other bind is skipped
when `GetLocalAircraft` comes back empty, and these have to work at the main menu.

## The instance registry — **built**

Each frontend document opens exactly one `EventSource('/stream')` (`telemetry-source.js` owns
the only one; the shell and every page read it second-hand), and each of those lands in its
own `HandleSseAsync` task that lives as long as the browser stays on the page. One live task
= one instance, so the server registers on entry and drops the entry in that method's existing
`finally`. `GET /soi-instances` lists them (`conn`, `cid`, `remote`, `upSec`) — a diagnostic,
and the way the registry was proven before anything was wired to it.

A document identifies itself with a **cid** it sends as `/stream?cid=…`: `crypto.randomUUID()`
kept in **`sessionStorage`**. Not `localStorage` — the id has to be the *same* across a reload
(a tablet that refreshes should stay the instance it was, and get its focus back rather than
becoming a stranger) and *different* in a second tab (two displays on one PC are two
instances). `localStorage` is per-origin and gets the second half exactly wrong. Read from the
map-tap iframe, `sessionStorage` resolves to the tab's store, which is the document we mean.
Both hops are guarded: `sessionStorage` throws in some private-mode browsers, and
`randomUUID` needs a secure context, which plain `http://` over the LAN is not — the fallback
id identifies the instance while it is connected but does not survive a reload.

The registry keys on a server-side connection number, not the cid. A duplicated browser tab
copies its `sessionStorage`, and so its cid; keying on that would let the copy evict a live
connection from the list, and let either one's disconnect remove the other.

One constraint this imposes on everything after it: **the frame is shared, deliberately.**
`GetFrameBytes` serializes each snapshot version once and every client writes the same bytes
(docs/performance.md #2). Per-client payloads would throw that away at exactly the moment
there are several clients. So SOI state rides in the *broadcast*: the frame names which
instance is focused, and each client compares that against its own cid. Nothing in the design
needs a private channel.

## What the plugin sends

Deliberately almost nothing. The plugin does not know what a page contains, and shouldn't:

- `soiTarget` — the focused `cid` — **built.** In every frame including the 1 Hz ping, so a
  display is focusable at the main menu. Every client sees it and compares it against its own
  id; the match draws itself as focused, the rest draw normally. It is server state rather than
  snapshot state, so it versions the frame cache separately — otherwise a target moving between
  two identical pings would never ship.
- `soiSeq` + `soiAct` — a counter and the last key pressed (`up`/`down`/`select`) — **built.** A
  client acts when the counter changes and ignores the fields otherwise, which makes the transport
  idempotent: a dropped or duplicated frame can't double-press a key, and the 10 Hz frame rate caps
  the input lag at ~100 ms (1 Hz at the main menu). The first counter a client sees is recorded
  without acting — presses made before it connected are history, not input. Both fields are present
  in every frame from the first, ping included, precisely so that rule can tell "no presses yet"
  from "a press just happened".

**SOI is opt-in.** Focus is empty until the pilot presses a SOI key, and never moves on its own
— so a mouse/touch user who never touches the keys never sees the ring, and the MFD pages stay
uncluttered.

- **A fresh display does not take focus.** Connecting changes nothing; the ring only appears once
  `SOI NEXT`/`PREV` is pressed.
- **`SOI NEXT`/`PREV`** move focus along the registered instances, oldest connection first,
  wrapping at both ends. From empty, either key picks the first (`NEXT`) or last (`PREV`) — that
  press is what activates SOI in the first place.
- **A display dropping only affects focus if it held it**, and then focus *clears* — it never
  jumps to another display, because that would be the ring appearing on a screen the pilot didn't
  pick. The next `SOI NEXT`/`PREV` re-picks from what is left.
- A duplicated tab copies its `sessionStorage`, so two connections can carry one cid. If a twin
  is still connected the display is still there, and focus stays put.

Connects and disconnects each arrive on their own threadpool thread and both can touch the
target, so every change takes one lock. `tools/soi-focus.test.js` models these rules and locks
them.

`POST /command` `soi.next` / `soi.prev` drive focus as well as the binds do, which is how it is
exercised without a controller.

**Focus is per instance, not yet per pane.** Pane count is something only the client knows (a
split has two, full view one), so cycling panes needs the client to report its panes first —
that arrives with the cursor, which is the thing that makes a pane distinguishable anyway.

## What the cursor moves over — **built** (classic)

The bezel already answered this. Every navigable thing in the classic shell is a key carrying a
`data-action`, and `mfdButton(key)` is the single activation entry. So the cursor is an index into
"keys that currently have a `data-action`", left bank then right, and `SELECT` is `mfdButton()` on
the one it stops on. The list is derived on every press, never maintained. No page knows SOI
exists, and every page with keys got it at once: MAIN's menu, WPN's weapon list and paging, MAP's
zoom rocker, LYT. In split mode the keys already carry `data-pane`, so a press lands on the right
pane without SOI knowing panes exist.

The cursor marks the **key**, and its overlay label when it has one. Marking only the label loses
the cursor exactly where it is most useful: WPN's weapon rows are keys with an action whose text is
drawn *inside* the page iframe, so no label exists to mark. Every action key has a key. Two things
rebuild labels without a page change — WPN's per-tick `placeWpnNavLabels` and `renderSplitLabels` —
so both re-apply the mark; the index is clamped rather than reset, so paging keeps the cursor
roughly where it was.

The first `NAV` press only reveals the cursor rather than also moving it: entering from the end the
key came from (`DOWN` → first, `UP` → last), so the first press of a session is predictable.

The flip side: controls that live *inside* a page iframe are invisible to it. TGT's filter
toggles and HUD's category rows are clicked, not keyed, and BDF/PAL are read-only panels — none
of them offer the shell anything but the single MAIN back-label. Fixing that means a page-level
cursor protocol (the shell forwards `up`/`down`/`select` into the iframe and the page decides
what it has), which is a second stage, not this one.

In practice they degrade rather than break: such a page contributes only its MAIN back-key, so a
display sitting on one has a one-item cursor and `SELECT` gets you out of it. That is the right
behaviour to keep even once the gap is closed — dropping focus off a page you can no longer key
your way out of would be worse.

Skipping them entirely, so `SOI NEXT` never lands on one, needs focus to be per *pane* rather than
per display: a display isn't skippable when the pilot may want the pane beside it. That waits on
pane reporting.

## Showing focus — **built** (classic)

> On the `soi-surfaces` branch this ring became per-**surface** (per pane in a split), a
> JS-positioned `#soi-ring` div — see "Planned: surface-level focus" below. The description here is
> the original whole-screen instance ring.

The focused display rings its whole screen in off-white, the way a real MFD says it — DCS
draws a bright box around the display area, and so does this. Unfocused displays draw nothing.

The ring traces the recess exactly: a `::after` on `.screen`, inset by the 6px padding and
sharing the iframes' 3px corner radius, so it lands on the boundary between bezel and glass. No
markup, no layout effect, and `pointer-events: none` so it can never eat a tap. It is drawn
above the overlay, so bezel labels and chips sit inside it rather than over it.

Off-white (`--no-label`) rather than the theme's green: green is what instruments report in,
and this is chrome saying where the controls are pointed — the same distinction the bezel's key
labels already make.

It frames the display, not a pane, so a split doesn't change it — the cursor's own amber mark is
what says which pane you are working in.

The telemetry tap owns the comparison: it is the only part of the frontend that knows this
instance's cid, so it posts a `soi` slice up to the shell as a plain boolean, and only when it
changes rather than ten times a second. Losing focus takes the cursor with it.

## The cursor mark

Amber, the theme's engaged colour: a filled label (`--no-ink` on amber) and a steady glow on the
key itself. Filled rather than outlined because `.overlay-item.on` and `.paging` already use both
outline idioms, and a cursor is a *position* rather than a property of the label — it has to read
at a glance from across the cockpit. The glow is distinguishable from `.key.lit`'s green
press-flash, which is momentary.

## Scope

> **Both layouts, per surface** as of the `soi-surfaces` branch (see "Planned: surface-level focus"
> below, now built). The notes below are from the original classic-only MVP.

Classic layout only. It is the layout whose navigation is already a flat, ordered, shell-owned
list of keys, so the cursor was nearly free there; the F-35's portals would need their own cursor
over `.nav-item` labels, and it has up to four portals per instance rather than two panes.
Everything server-side — the instance registry, the target, the action counter — is
layout-agnostic and carries over unchanged.

## Still open

- **Pages with in-iframe controls can be reached but not operated** — TGT's filter toggles, HUD's
  category rows. Needs a page-level cursor protocol. (On the F-35, WPN's weapon rows are the same
  kind of gap — see step 3.)
- **Ordering of `SOI NEXT`.** Connection order is what the server has, and it is neither stable
  nor spatial — reconnect a tablet and it moves to the end of the ring. A named or user-ordered
  instance list is the fix, and it needs somewhere to live.
- **Key repeat.** The binds are edge-driven, so `NAV DOWN` is one step per press. Holding it to
  walk a long list would need a repeat cadence, which the plugin would own since the counter is
  its own. Edge-only is deliberate for now: it can't overshoot.
- **Does an unfocused display need to say anything?** It shows nothing, which is the right
  default — but a rig with four displays wants to see *where* focus went without looking away
  from the one it just left. A brief marker on every display when the target changes is cheap, if
  it turns out to be missed.

## Planned: surface-level focus (Option B) — the F-35, and the split done right

**Status: on the `soi-surfaces` branch — all three steps built.** Server, classic per-pane client,
and F-35 portal client are done; SOI now works in both layouts, per surface.

Today the unit of SOI focus is an *instance* (a `cid` = one document). That already strains the
classic split — the whole screen rings and the cursor walks *both* panes as one flat list — and it
doesn't fit the F-35 at all, whose glass is up to four independently-navigable portals. The fix is
to make the unit of focus a **surface**: an instance contributes 1 surface in full view, 2 in a
split, and N = its live `portals.length` on the F-35. Focus addresses **(cid, paneIndex)**; `SOI
NEXT`/`PREV` walk a flat ring **instance-major, surface-minor** — instances in connection order,
and within each, its surfaces in visual order (classic top→bottom, F-35 left→right) — so a
3-portal F-35 steps portal 0 → 1 → 2 → next instance, wrapping. Only the focused surface rings, and
the cursor is scoped to that surface's nav.

The server change is **backward-compatible**: a client that never reports its surface count stays
at `PaneCount = 1` and behaves exactly as today (whole-instance focus). So it lands in three stages.

### Step 1 — server (`TelemetryServer.cs`, `CommandDispatcher.cs`) — **built**

- `MfdInstance` gains `int PaneCount = 1`.
- Focus state gains `_soiTargetPane` (int, `-1` = none) beside `_soiTargetCid`, under the same
  `_soiLock` / `_soiVersion`. `SetSoiTarget(cid, pane)` replaces the string-only setter.
- `SoiCycle(dir)` walks the flat ring built from `Instances()` × `0..PaneCount-1` (deduped by cid so
  twins collapse), instance-major/surface-minor, wrapping; from empty, `NEXT`→first, `PREV`→last.
- New `soi.panes { cid, n }` command (`CommandEnvelope` gains `n`): a `POST /command` isn't tied to
  the SSE connection, so it carries the `cid`; the handler sets that instance's `PaneCount`. If that
  cid is focused and `_soiTargetPane >= n` (a merge shrank the glass), **clamp** to `n-1` — the one
  sanctioned focus move without a keypress, confined to the same instance.
- `SoiJson()` gains `"soiPane":<int>` (`-1` when unfocused), in the ping too.
- `SoiReleaseOnDisconnect` is unchanged in spirit: a focused instance dropping (no twin) clears;
  focus never hops to another display.
- `soi-focus.test.js` extends its model to the flat surface ring: cycling, wrap, clamp-on-shrink,
  clear-on-disconnect.

### Step 2 — classic client (`mfd.js`, `telemetry-source.js`) — **built** (the split upgrade)

The tap posts its cid up on every `hello` (`soi-cid` — including after an SSE reconnect, when the
server has reset the count) and carries `pane` on the `soi`/`soi-act` messages. The shell remembers
that cid and reports `soi.panes {cid, n: splitMode ? 2 : 1}` on load and split toggle. `soiKeys()`
scopes to the focused pane (`data-pane` 'top'/'bot'), so the cursor stops spanning both panes;
moving to a different surface drops the cursor, revealed fresh on the next NAV.

The ring is no longer `.screen.soi::after` (whole screen) — it's a JS-positioned `#soi-ring` div
(`positionSoiRing`) sized to the focused surface's measured box: the recess (the map iframe) in full
view, one pane in a split. Measured, not CSS-placed, because a split pane is flex-sized — V_WIDE is
2:1 — and only reading its box rings it exactly. Repositioned on the `soi` message, split
enter/exit, axis flip, and window resize.

### Step 3 — F-35 client (`f35.js`, `f35.css`) — **built** (the new surface)

`f35.js` grew the SOI handling it lacked entirely: it listens for `soi-cid`/`soi`/`soi-act` from
the tap, reports `soi.panes {cid, n: portals.length}` on load and on every grip merge/split
(`refreshGlass`, the one "glass changed" hook), rings the focused portal (a `.soi` class → a white
inner ring in `f35.css`), and walks a cursor over that portal's enabled `.nav-item`s — a class on
the div, since the glass has no physical keys. `SELECT` clicks the cursored item through its own
wiring; a press that navigates the portal drops the cursor, one that stays put (paging, a map
control) keeps it — the same rule the bezel uses, told apart by the portal's `page()`. The portal
calls back (`onNavRendered`) after each nav rebuild so the focused one re-applies its cursor, the
F-35 twin of the bezel's post-rebuild `renderSoiCursor`. `focusedPortal()` bounds-checks the index,
so the frame between a merge and the server's clamped target rings nothing rather than the wrong
box.

**Gap:** the cursor walks a portal's `.nav-item`s (MAIN menu, page nav, WPN's MAIN/PREV/NEXT) but
not WPN's weapon rows — on the F-35 those are `.wpn-hit` overlays, not nav items, so unlike the
bezel (where weapon rows *are* keys) SOI can page WPN but not pick a weapon on it. Reaching them
means folding `.wpn-hit` into the cursor's target list in reading order; deferred.

### Deferred

Surface **identity is by index** (`0..n-1`) for now — simplest, and the server already thinks in
ordered lists. The soft spot: a merge that removes a portal *left of* the focused one shifts indices
for a frame until the re-report + clamp settle. If that ever reads wrong, upgrade to client-reported
stable surface IDs (`(cid, surfaceId)`), strictly more protocol.
