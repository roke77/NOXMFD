# Extended Keybinds page (/keybinds)

A standalone page (like `/f35` — opened directly, not an MFD split pane) for binding the
mod's *extended keybinds*: cockpit functions the game has no native keybind for. It is the
only keybind UI — the F1 (ConfigurationManager) menu shows none of these entries, though
the values still persist in the plugin `.cfg`.

Reached from **KEY** on MAIN in both layouts (`BEZEL_EXTRAS.main` / the F-35's `MAIN_EXTRAS`),
or by opening `/keybinds` directly. KEY is not a page either shell can host — clicking it leaves
the document, the way LYT's F-35 choice does, so it is dispatched ahead of the bezel's split-pane
branch and sits in the F-35's `LINKS` table rather than `F35_PAGES`. The page's `< MAIN` link goes
back to `/`, which the sticky-layout head guard resolves to whichever shell is current.

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
| Gear Up / Gear Down | dedicated raise/lower (stock bind is a toggle); no-op if already there, mid-transition, or on the ground | edge |
| Cycle Guns / Missiles / Bombs | select within the class — see below | edge |
| Gun Trigger / Weapon Release | two-stage class fire keys — see below | held |

### Weapon selectors (WeaponSelectors.cs)

Alongside the game's single active-weapon selection, the mod remembers a **gun** choice and
a **missile-or-bomb** choice ("soft selections", pointing at the same aggregated loadout
entries the WPN page lists). Classification uses the game's public `WeaponInfo` flags:
`gun`; `bomb`/`glideBomb`; missiles are the `missile` flag plus flagless launched ordnance
(rockets carry no flag), excluding jammer/cargo/troops/sling.

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

# Exploring: SOI (sensor of interest)

**Status: in progress on the `soi` branch.** The instance registry and the focus target are
built; the binds and the cursor are not. Kept in this doc because its whole user-facing surface is
five more rows on this page. Sections below are marked where they describe working code.

Borrowed from DCS: one display at a time is the *sensor of interest*, and a fixed set of
HOTAS keys drives whichever display that is. Here a "display" is one MFD instance — a
browser somewhere on the network — and, within it, one pane. Focus moves; the keys don't.
The point is to work a screen you are not touching: a tablet clamped to the rig, a second
monitor, a phone velcroed to the throttle.

Five binds:

| Bind | Effect on the focused pane |
|---|---|
| `NAV UP` / `NAV DOWN` | move a cursor through the pane's line-select labels |
| `SELECT` | activate the cursored label — the same thing clicking that bezel key does |
| `SOI NEXT` / `SOI PREV` | move focus to the next/previous pane, wrapping across instances |

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
- `soiSeq` + `soiAct` — a counter and the last action (`up`/`down`/`select`). Not built. A
  client applies the action when the counter changes and ignores it otherwise, which makes the
  transport idempotent: a dropped or duplicated frame can't double-press a key, and the 10 Hz
  frame rate caps the input lag at ~100 ms.

Focus is never left unset while a display is open:

- **The first display up takes it**, with no key pressed. A later one never steals it.
- **`SOI NEXT`/`PREV`** move it along the registered instances, oldest connection first, wrapping
  at both ends. From no focus at all — only possible with nothing connected — either key still
  lights up the first or last it finds.
- **A display dropping only moves focus if it held it**, and then to the oldest still connected;
  leaving the pilot unfocused because a screen they weren't looking at went away would be worse
  than picking the obvious survivor. Focus goes empty only when the last display closes.
- A duplicated tab copies its `sessionStorage`, so two connections can carry one cid. If a twin
  is still connected the display is still there, and focus stays put.

Connects and disconnects each arrive on their own threadpool thread and both can move the
target, so every change takes one lock — "is anything focused?" and the write that answers it
have to be a single step, or two displays connecting together both see nothing focused and the
second silently steals it. `tools/soi-focus.test.js` models these rules and locks them.

Until the binds exist, `POST /command` `soi.next` / `soi.prev` drive focus — which is also how
it is exercised without a controller.

**Focus is per instance, not yet per pane.** Pane count is something only the client knows (a
split has two, full view one), so cycling panes needs the client to report its panes first —
that arrives with the cursor, which is the thing that makes a pane distinguishable anyway.

## What the cursor moves over

The bezel already answers this. Every navigable thing in the classic shell is a label placed
on a physical key, `mfdButton(key)` is the single activation entry, and in split mode each
key already carries `data-pane`. So the cursor is an index into "keys of this pane that
currently have a `data-action`", `SELECT` is `mfdButton(k)` on it, and the highlight is one
more class on the overlay label. No page needs to know SOI exists, and every page that has
bezel keys gets it at once: MAIN's menu, WPN's weapon list, MAP's zoom rocker, LYT.

The flip side: controls that live *inside* a page iframe are invisible to it. TGT's filter
toggles and HUD's category rows are clicked, not keyed, and BDF/PAL are read-only panels — none
of them offer the shell anything but the single MAIN back-label. Fixing that means a page-level
cursor protocol (the shell forwards `up`/`down`/`select` into the iframe and the page decides
what it has), which is a second stage, not this one.

**So the MVP does not focus them at all.** `SOI NEXT`/`PREV` skip any pane whose page has
nothing to cursor over — which is derivable rather than a hand-kept list of page names: a pane
is eligible when its page offers more than the one MAIN back-label. That admits MAIN, MAP, WPN
and LYT, and excludes TGT, HUD, BDF, PAL, TGP, AVN and RWR by the same rule, with no per-page
knowledge anywhere.

Focus is only *tested* when SOI moves it, though, not held continuously — if you SOI into a
pane and then navigate that pane onto TGT, focus stays, with MAIN as the one thing the cursor
can reach. Dropping focus there would strand you on a page you can no longer key your way out
of, which is worse than a pane with a one-item cursor.

## Showing focus — **built** (classic)

The focused instance draws a small boxed **SOI** label in its **top right**; unfocused
instances draw nothing. That corner already holds the shell's indicator stack (PINNED,
FOLLOW), so SOI is one more chip in it rather than new chrome — `indicatorOrder` even gets the
stacking right for free, since it records activation order.

It is off-white (`--no-label`) where the other two are amber. PINNED and FOLLOW report a
control *this* screen has engaged; SOI reports which screen the controls are pointed at, which
is chrome, and `--no-label` is the token the bezel already uses for that distinction.

The chip is the whole indication for now. A per-pane marker — which of a split's two panes
holds the cursor — can wait until the cursor exists, because the cursor highlight already says
it. Top right is provisional in the sense that the F-35 has no indicator stack to put it in,
so that layout's answer may end up elsewhere.

The telemetry tap owns the comparison: it is the only part of the frontend that knows this
instance's cid, so it posts a `soi` slice up to the shell as a plain boolean, and only when it
changes — otherwise the indicator stack would rebuild ten times a second to say the same thing.

## MVP scope

Classic layout only, and only the panes with something to cursor over (above). It is the
layout whose navigation is already a flat, ordered,
shell-owned list of keys, so the cursor is nearly free there; the F-35's portals would need
their own cursor over `.nav-item` labels, and it has up to four portals per instance rather
than two panes. Everything above that is server-side (instance registry, target, action
counter) is layout-agnostic and would carry over unchanged.

## Open questions

- **`Poll()` is aircraft-gated.** It returns early when `GetLocalAircraft` gives nothing, so
  today no bind fires at the main menu. SOI keys must work there — navigating MAIN is the
  obvious thing to do while waiting for a mission. `BindDef` needs a "doesn't need an
  aircraft" flag, or `Drive` needs to take a nullable one.
- **Does focus survive a reload?** Only with the client-supplied `cid`. Worth doing up front;
  retrofitting identity is worse than starting with it.
- **Does an unfocused instance need to say anything?** It shows nothing today, which is the
  right default — but a rig with four displays wants to see *where* focus went without looking
  away from the one it just left. A brief marker on every instance when the target changes is
  cheap, if it turns out to be missed.
- **Ordering of `SOI NEXT`.** Connection order is what the server has, and it is neither
  stable nor spatial — reconnect a tablet and it moves to the end of the ring. A named or
  user-ordered instance list is the fix, and it needs somewhere to live.
- **Key repeat.** Held `NAV DOWN` should walk the list, so these are held binds with a repeat
  interval rather than edge binds — the plugin owns that cadence, since the counter is its own.
