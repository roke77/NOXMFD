# AKF

Part of the **MD** (Mission Data).

A replica of the game's own kill-feed ticker, split into an ALL column (every kill) and a PLAYER
column (yours — weapon name included where resolvable, plus lines for when you're shot down or
your own ordnance is intercepted). Session kill tally, funds gained/spent, and current rank are
shown below the feeds.

Squadmates show under their squad designation with their Steam name in parentheses, e.g.
`TALON 1-2 (Roke) [F/A-26]` ([SQD](sqd.md#names-in-the-game)).

The native kill-feed ticker this replicates can be hidden from the in-cockpit HUD with
[HUD](hud.md#declutter)'s **FEED** toggle, without affecting this page.

![AKF page](images/AKF.png)

## ALL / PLAYER split

A divider with triangle grips separates the two feeds. Dragging it resizes the ALL/PLAYER column
widths. Dragging a column below a minimum width collapses it, leaving only an arrow pointing
toward the remaining column. Clicking the arrow restores the default split.

![AKF page with the ALL column collapsed](images/AKF_COLLAPSED.png)

The split (custom or collapsed) is not persisted; reloading the page resets it to the default.

## Density toggle

DETAILED/COMPACT toggle, top right. DETAILED shows the full line: `Attacker verb Victim with
Weapon`. COMPACT truncates each unit name to its first word, replaces the verb with a `▸` glyph,
and replaces `with` with a `•` glyph. The weapon name is unaffected in both modes. The setting
applies to both feeds and only changes how entries render.

![AKF page in COMPACT mode](images/AKF_COMPACT.png)

## PAD cursor

The resizer and density toggle are drivable from a HOTAS [PAD cursor](keybinds.md#pad-cursor).
Holding Select over the resizer drags it. A Select tap activates the density toggle, or restores
the split if the resizer is collapsed.

## Kill log (CSV)

Every kill in the ALL feed can also be written to a CSV file, one file per mission, for keeping
squadron tallies in a spreadsheet. Off by default: set `[Kill log] Enabled = true` in
`BepInEx/config/com.roque.NOXMFD.cfg`, or toggle it in the in-game BepInEx config menu. It takes
effect immediately, including mid-mission.

Files go to `BepInEx/NOXMFD-kills/`, named `kills_<date>_<time>_<mission>.csv`. A file is created
on the mission's first kill. Columns:

| Column | Content |
|---|---|
| `time` | Local time of the kill, `yyyy-MM-dd HH:mm:ss`. |
| `killer` | Killer's unit name, as in the feed (player aircraft carry the player's name). Blank when the game credits no one. |
| `killer_faction` | Killer's faction. |
| `victim` | Victim's unit name. |
| `victim_faction` | Victim's faction. |
| `type` | `aircraft`, `vehicle` (ground units), `ship`, `building`, or `missile` (an intercepted munition). |
| `weapon` | Best-effort, same as the PLAYER feed. Blank for gun kills. |

The log only holds kills this game client saw. Joining a mission late misses earlier kills.
