# F-35 layout — build log

Historical narrative: how the F-35 layout was designed and built, stage by
stage, plus the open-questions log from that work. `docs/layouts.md` is the
current-state architecture reference; this document exists so that reasoning
doesn't have to be interleaved with it (docs/refactor-scan.md step 9). Written
as it happened, so later sections describe things earlier sections changed —
read stage by stage, not as a snapshot of today.

## Staged approach

Prove the seam with a pure refactor, then add the second layout as a
consumer.

### Stage 1 — the seam ✅ done

Extracted the navigation model out of `PAGES`: dropped `key`/`slot`, kept
`label`/`action`, routed the bezel through it. Zero behavior change, proven
by a data-equivalence check against the old tables before deleting them.

It answered two of the open questions below, and removed a live duplication
bug source: `label`/`action` had been declared twice for five pages.

### Stage 2 — the F-35 layout ✅ done

Built (`src/web/shell/f35/`, served at `/f35`):

- borderless frame, no bezel; labels drawn *on* the page
- two placement modes: `edge` (the bezel's left key bank, minus the bezel)
  and `center` (MAIN's own 3-column block)
- every page hosted, including WPN with layout-supplied rects
- the portal split, driven entirely by the corner grips
- shared action dispatch; `NAV` unmodified

#### Portals

A **portal** is an independent MFD: it owns everything two screens must not
share — which page is up, where its WPN list is paged to, whether its map
follows. The shell keeps only the telemetry cache and the tap.

The glass is **four slots** wide, and a portal fills one slot or two — never
three. So any two *adjacent* portals may merge, and nothing larger. `flex-grow`
carries the span, which is the whole resize arithmetic: with `flex-basis: 0`
every slot is the same width, so growing a portal to 2 gives it exactly two
and its neighbours keep theirs. No wrapper elements, no percentages.

Because a merge joins two and no more, at least two portals always remain:
**the glass is never one screen.** The real PCD isn't either.

##### The arrangement rule

Lives in `f35-glass.js`, pure and pinned by `f35-glass.test.js`. Every
arrangement is some set of adjacent merges that don't overlap, which is
exactly five:

```
1 2 3 4      (1 2) 3 4      1 (2 3) 4      1 2 (3 4)      (1 2) (3 4)
```

Nothing else is reachable: `(1 2)` and `(2 3)` would both want portal 2, and a
triple would need a merged portal to merge again. So a portal beside a merged
one has no grip at all, and simply waits — in `1 (2 3) 4`, portals 1 and 4
both do.

**Layout is not the whole story.** `(1 2)` merged from the left and from the
right occupy the same slots but keep different pages — the survivor is
whoever pressed. So the five layouts cover **six states**, and `ate` (which
side a portal swallowed) is what tells them apart. A split needs it to put the
newcomer back on the side that was eaten; getting that backwards silently
swaps the pilot's screens.

##### The grips

The corner grips are the only control: no URL, no presets. A grip sits in the
corner facing what it acts on, and its direction says what:

- **outward** — take the neighbour on that side.
- **inward** — give back the slot it took, splitting in two again.

An unmerged portal gets **one** grip, and it faces the **centre of the glass**:
portals 1 and 2 reach right, 3 and 4 reach left. Both neighbours of a divider
could offer to merge across it, but the two offers differ only in which page
survives — so one grip per divider costs a *choice*, never a layout, and all
five arrangements stay reachable.

The centre divider is the one place two grips meet, since the portals either
side both face it. That makes `(2 3)` the only merge reachable from either
direction — asymmetric, and deliberate.

The survivor of a merge keeps its page and everything on it, and just gets
wider. The absorbed portal is destroyed — its iframe and any map stream go
with it — and comes back fresh on MAIN.

##### MAP costs a stream here

The tap sits behind every portal, so it could only ever be *shown* to a portal
covering the whole glass, and no portal ever does. Every MAP portal therefore
mounts its own `/map-view?bare` and streams alongside the tap. The bezel pays
exactly the same in split mode, and ignores the duplicate telemetry the same
way (`event.source` must be the canonical map). `FLW`/`Z+`/`Z-` route to the
portal's own map, and `follow` is per-portal — it routes by source, as the
bezel's does.

#### The frame

Every portal is boxed on all four sides, as the reference display frames each
of its windows. A portal is a whole MFD, so it is drawn as a whole box rather
than merely divided from its neighbour — which is also what keeps the rule free
of the arrangement: merging removes a portal and its frame goes with it, and the
survivor's own frame is already the right shape. There is no "first portal has
no border" case, and no separator elements: a portal's frame is its own.

Where two frames meet they overlap, by a negative margin rather than a dropped
border, so a seam is 2px and not 2px twice while every portal still owns all
four of its edges. The same trick pulls the row up under the master strip, whose
bottom edge the portals' top edges would otherwise double. That margin sits on
the row, not the strip — the strip has to keep drawing that edge when the
portals are away, since the layout picker takes their place and draws no frame.

The spans survive it: `flex-basis: 0` means the margin is redistributed by grow,
so four portals come out equal to the pixel. A merged one lands a shade under
twice its neighbour (640 vs 322 at 1280) because it covers two slots but draws
one frame, keeping the 4px the second would have spent — a seam's width, and not
worth making a portal's size depend on its span.

#### Decluttering — `?nochrome`

The master strip carries the avionics flags, the THRL/FUEL gauges, the mission
name and the ownship grid. AVN and MAP used to draw all four themselves, so on
this layout they were drawn twice — and on a quarter-width portal the
duplicates cost space the pages needed.

So each of those two pages grew an option: `?nochrome` means *my host already
shows my own-ship readouts, so I won't repeat them*. MAP drops the mission bar
and the GRID chip, keeping the map; the F-35 sets it there. The bezel sets it
on nothing and renders exactly as before — which is the test that this is a
page's option and not a layout's reach-in.

> **2026-08-15 — AVN's `?nochrome` was removed entirely, not just left unset.**
> It used to hide AVN's own-ship readouts *and* leave its damage silhouette
> filling the freed space — the actual reason the option existed (see the note
> below, kept for the record). That silhouette moved to a new **AFM** page this
> session, so `?nochrome` had nothing selective left to hide: hiding AVN's
> entire content would also have taken its status tiles, which are bezel/
> portal-actuated toggles (and directly clickable on the page itself now, see
> "AVN's status tiles" below) — the only way to flip gear/radar/etc from this
> layout. Rather than give AFM its own `?nochrome` to fill the gap, the
> `?nochrome` handling was deleted from `avn.html`/`avn.css` outright and the
> F-35 now requests plain `/avn`, same as every other page — accepting a little
> duplication of FUEL/THRL with the strip in exchange for keeping AVN's content
> (and its only toggle controls) intact. `?nochrome` is MAP-only now.

**It is a URL flag, not a message.** Every other host→page instruction here is a
postMessage (`avn-layout`, `orient`), but those adjust a page that is already
painted, while this one decides *what to paint*. A message arriving after first
paint would flash the readouts up and then pull them, on every mount — and a
portal glass mounts pages constantly. An inline read in each page's `<head>`
puts the class on `<html>` before the body parses, so they simply never appear.
(`?bare`, the other flag in these URLs, is a convention no page has ever read.)

**(Historical — kept for the reasoning, superseded by the note above.) AVN's
silhouette used to take the freed space on its own.** The status tiles were
inside the header, and `layoutAvnFrame` positioned the frame below the header's
*measured* bottom on every paint — so dropping them moved the silhouette 42px up
and made it 42px taller at 320×640, with nothing told to do it. The bars were
absolutely positioned and were never in anything's way; they just went. AVN has
no silhouette left to reflow this way — it's on AFM now.

#### The palette — what the layout colours, and what it doesn't

The line is **chrome versus instrument data**, and it is worth stating because
it is not obvious from any one rule:

- **Teal** (`--no-teal`) is what the layout *draws*: the portal frames, the nav
  labels on the glass, the corner grips, the master strip and its fullscreen
  button.
- **The theme's palette** is what anything *reports*, wherever it sits. The
  strip's gauges and avionics flags are green/amber/red exactly as on the AVN
  page; the engaged state stays amber; the mission chips stay the map page's
  green. The strip's URLs are off-white — an address you type somewhere else is
  chrome, but it is reporting, not framing.
- **Pages are untouched.** Nothing under `pages/` reads `--no-teal`, so a page
  renders identically here and under the bezel.

`--no-teal` lives in `theme.css` with every other colour, alongside
`--no-label`, which is the bezel's equivalent: the two layouts frame themselves
differently, so each names its own colour rather than one redefining the other's
per document. Defining it in the shared theme costs nothing — what matters is
that no page reads it, and that is a convention, not a mechanism.

#### A portal is not the glass

Two bugs, one mistake: something sized against the **viewport** while living
in a **portal**. Both were correct until the portal stopped being the whole
screen, and both only appeared at four.

- MAIN's label grid used a `6vw` column gap — 77px of the *glass* inside a
  320px portal, so seven of ten labels overflowed. The portal is now a CSS
  container (`container-type: size`) and the grid sizes in `cqw`/`cqh`.
  **There are no viewport units left in this layout**, deliberately.
- WPN's weapon image collapsed to a sliver. See "Per-portal orientation"
  below.

The rule for anything added here: measure the portal, never the window.

### Stage 3 — selection ✅ done

**Both layouts can now switch to the other, live.** Both offer it as **LYT** on
MAIN — the same name in the same place, so the way across doesn't have to be
learned twice — and each then draws the chooser in its own idiom rather than
sharing a screen:

- The bezel's LYT opens a LAYOUT page that is two left-bank labels and nothing
  else: CLASSIC, F-35. (No MAIN back-item — picking CLASSIC already lands back
  on this shell's MAIN, so a separate way back would be redundant with it.) It
  draws no panel — every page in this shell puts its items beside a physical
  key, and a chooser is navigation, so it reads as one.
- The F-35's LYT swaps the portals for a two-item chooser centred on the glass.
  Picking F-35, the layout you are already on, is how you leave it — the mirror
  of CLASSIC on the bezel's page.

Both mark the layout you are on in the theme's engaged amber. Neither needs
state to do it: each document *is* one of the layouts, so its own item is marked
where it is declared and the other is simply somewhere else.

Neither chooser is a page. Choosing a layout is the shell's business, and a page
must render the same under either shell — so the F-35's takes over the whole
portal column and the bezel's is three labels its shell places. That also settles
"full view only" for free: the bezel's LYT has no `PAGE_URL` entry, so it cannot
be a pane, and
`setSplit`'s existing fall-back (`PAGE_URL[currentPage] ? … : 'main'`, written
for TGT) lands both panes on MAIN if you split from it. Not one line enforces
the rule.

`.overlay-item.on` is the only state a bezel label has ever carried — the rest
name a page, while CLASSIC and F-35 name a choice and one of them is already
made.

LYT cannot live in `NAV` even though both layouts show it: that list is shared
and pinned at MAIN's six items, one per bezel left-bank key. Each layout names it
in its own extras table instead — `BEZEL_EXTRAS.main` and `MAIN_EXTRAS` — which
is the same arrangement HUD, KEY, BDF and PAL have. It also spent the prediction
at `mfd.js:87`: `fullViewSlot` fills the left bank in order and MAIN's six fill it
exactly, so MAIN is **the first screen this shell has had to place labels
anywhere else**. It does that by count rather than by a placement table: the
merged list is sorted alphabetically and dealt out left bank first, then right.

**The choice now sticks — client-side, not server-side.** Each chooser writes the
picked layout to `localStorage.layout` (`setLayout` in `mfd.js` / `f35.js`) before
navigating. A guard inline in each shell's HTML `<head>` reads that value on load
and `location.replace`s to the other document when it names the layout this one is
*not* — so a fresh load, a reload, or a tablet opening `/` all land on the last
choice. The guard runs before paint, so there's no flash of the wrong shell.

Two decisions worth recording:

- **Client-side, not a BepInEx `ConfigEntry`.** We considered a server-side
  preference that would make `/` serve either shell. localStorage is the smaller
  diff, needs no plugin or route change, and matches where the rest of the client's
  state already lives (map follow, zoom, WPN paging). The `ConfigEntry` path stays
  open if the preference ever needs to be set from *outside* the browser (e.g. a
  default baked into the mod), but nothing needs that today.
- **Each document guards only against the *other* layout's value**, never its own.
  The classic doc redirects only on `'f35'`, the F-35 doc only on `'classic'`; an
  unset or unrecognised value redirects nowhere (root stays classic by default).
  That asymmetry is what guarantees termination — resolving lands on a stable
  document in one step and cannot ping-pong. Locked by `shell/layout-sticky.test.js`.

The URL still names the layout (`/` vs `/f35`); localStorage only decides which one
a bare load resolves to.

## The gauges — where the strip's styling came from

> **2026-08-15 — the strip's gauges are unaffected, but their AVN references
> below now point at deleted code.** This section describes the F-35 strip's
> *own* self-contained bar widget (`.ms-tube-inner` etc. in `f35.css`/`f35.js`),
> built at the time by copying AVN's then-current vertical-bar styling and
> reusing `avn-throttle-policy.js` (a pure logic module, still there and still
> shared) for the MIL/AB math. The strip never imported AVN's CSS/DOM directly,
> so nothing here broke when AVN moved from bars to circular gauges this
> session — but `avn.css .avn-vbar-tube` and `positionAvnBarValue`, named below
> as the styling/behavior this was copied from, no longer exist on the AVN page.
> Left as written for the reasoning; treat those two names as history, not a
> place to go looking.

THRL and FUEL cost no telemetry: `fuel` and `throttle` were already in the `avn`
slice driving the flags.

The AVN page's own bars could not come along. They are absolutely positioned and
vertical, sized from rects the shell forwards, and their `%` readout tracks the
fill's tip in pixels on every paint — none of which survives a ~120px trough in a
one-row bar. What travels instead is the **rule**, not the widget:
`avn-throttle-policy` gives the MIL/AB split, the zone, and the readout string,
so the strip cannot drift from the gauge it summarises. It earns its keep
immediately — at 40% throttle against an 0.8 abStart the bar fills to 40% but
reads `50%`, because the fill is the raw axis while the text is rescaled within
the zone, and at the detent it reads `MIL` rather than `100%`.

The tube is AVN's own (`avn.css .avn-vbar-tube`), turned on its side: the 2px
green frame around a near-black trough, the fill inset within it, and AVN's
transition timings on width instead of height. The boot loader's `.ms-bar` is
left to be a progress bar. Sizing is the row's: the gauge block takes all the
slack between the mission block and the flags, and each trough takes whatever
its label and number don't.

Two details are carried over deliberately:

- **Only FUEL is segmented.** The 10% segment gaps and the halfway marker are
  `content: none` on AVN's throttle tube, which says what it has to say with the
  MIL/AB split instead. The gaps are 3px rather than AVN's 6px — same 10% pitch,
  but read across ~120px of width, 6px of every 12px is more gap than gauge.
- **The leader lines don't come.** AVN's `¯\___` SVG tracks each fill's tip and
  needs a per-paint measurement; there is no room for it in a one-ninth-tall bar,
  and the `%` readout says the same thing sitting still.

That growth is also what now holds the flags right. `flex-grow` resolves before
auto margins are offered any free space, so `.ms-flags`'s `margin-left: auto` no
longer does anything while the gauges are there; it stays as the fallback for if
they ever aren't.

When the strip runs out of room the order of giving way is deliberate: the
troughs floor at a `min-width` (a flex item's default `min-width: auto` stops
the block at its min-content), so it is the mission name — the one item that
sets `min-width: 0` — that ellipsises first. At an 87-character name the troughs
sit exactly on that floor and the flags and FULLSCREEN have still not moved.

The MIL/AB split comes across too, and it is the one place the strip does better
than the page it copies. AVN sizes that gradient in px (`--tube-inner-px`) so
the green→red boundary stays pinned to a fraction of the tube while the fill
grows past it — and it remeasures that width on every paint, inside
`positionAvnBarValue`, the same function that places the leader lines this strip
doesn't draw. So the strip asks CSS instead: `.ms-tube-inner` is a
`container-type: inline-size` container and the gradient is sized in `100cqw`.
The boundary lands identically with nothing measured and nothing to keep in step
with a paint loop.

One thing still has to be repeated: FUEL's warning levels (caution 0.25,
critical 0.10) live at AVN's call site rather than in a policy module, so the
strip states them again. They must agree, or the strip and the page would
disagree about the same tank.

## Open questions

### Settled by building it

- **Are labels derivable from the ordered list, or do they need placement
  hints?** Both, split by view. **Full view is derivable** — item *i* → slot
  *i* down the left column, identically for both layouts (`fullViewSlot`,
  `cellOf`). **Split is not** *for the bezel*: MAP deliberately groups its
  zoom rocker on the right, so it needs `SPLIT_SLOTS`. The portal model turned
  out to need no hints at all — a portal is a whole MFD, so it places labels
  exactly as full view does. The problem was the bezel's, not the split's.
- **Are HIDE SHELL / FULL / PIN / SWAP part of the navigation model?** No —
  layout-owned chrome. `nav-model.test.js` now enforces their absence.
- **One CSS bundle or two?** Two. `f35.css` shares no structure with
  `mfd.css`; only the `theme.css` tokens are common. That file is the palette
  both layouts and every page draw from — including the colours only one of
  them uses: `--no-label` is the bezel's off-white chrome and `--no-teal` the
  F-35's, each named rather than one being redefined per layout, since the two
  frame themselves differently. A layout's colour being *defined* in the shared
  theme costs nothing; what matters is that no page reads it, which keeps a page
  rendering the same under either shell. That is a convention, not a mechanism.
- **Where does split state live once splits differ per layout?** In the
  portal. Everything two screens must not share (current page, WPN paging,
  follow) belongs to the portal; the shell keeps the telemetry cache and the
  tap. The navigation model carries no split geometry, as planned.
- **Per-portal orientation** — confirmed, and now built. A quarter portal is
  320×720: genuinely portrait on a landscape screen. Reporting the window's
  orientation left WPN's 2:1 weapon image unrotated in a tall narrow box,
  collapsed to a ~124×62 stripe. Each portal now measures its own box
  (`forwardOrientation`), and the image turns 90° to fill the column.

  **This is a deliberate divergence from the bezel, not a bug in it.** The
  bezel reports the window's orientation on purpose: its panes are
  wide-and-short, so a pane measuring itself would call a portrait device
  landscape. Portals are the opposite shape and need the opposite rule. Full
  view and halves are unaffected — a portal that *is* the window measures the
  same as the window.
- **Does a portal drive WPN's `compact` or `full` profile?** `full`, with
  rects, at every portal count. Once the portal reports its own orientation,
  `full` renders correctly at 320px wide, so `compact` isn't needed — the
  profile split turned out to be about *shape*, which orientation already
  carries, rather than about size.

### Still open

- **What else the master strip carries.** Connection status and the server
  URLs now live in the strip (see above), alongside the avionics flags — the
  layout's first chrome wanting telemetry, settled. What *else* it should carry
  (warnings, comms, IFF) is still open, as is the deferred collapse.
- **A portal's own page set.** Every portal currently offers all of `NAV`.
  Four portals showing four MAINs is a plausible default but not obviously
  the right one, and the reference shows each portal with a fixed role.
- **Uneven portals.** A portal is one slot wide or two, so the glass only ever
  divides on slot boundaries — no dragging a divider to 30/70. The reference
  suggests fixed roles rather than dragged widths, so this may never be
  wanted; noting it because `SLOTS` and the span are where it would go.
- **Triples.** Deliberately excluded: a merged portal offers no merge, so
  `(1 2 3)` and a full-width portal are unreachable. Allowing them would bring
  back full view, and with it the one case where a portal could show the tap
  instead of running its own map.
