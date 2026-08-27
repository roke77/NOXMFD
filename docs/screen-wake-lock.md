# Screen wake-lock control

## Status

**Shipped** on branch `screen-wake-lock`, following this plan closely. Kept as a historical planning
record per this repo's convention for `docs/` design docs — see below for where the shipped result
differs from what was originally planned here.

- The shared `WakeLock.createController` module, both shells' wiring, the sun icon token, the
  `.key.icon.on` amber rule, the F-35 `.nav-item.on` reuse, and the `WAKE LOCK FAILED` banners all
  landed as designed. `src/web/shell/wake-lock.test.js` covers the 8 scenarios this doc's "Test
  plan" section calls for; the full JS suite is unaffected.
- CLASSIC's WAKE key ended up between **FULL** and **PIN** (`HIDE, FULL, WAKE, PIN, SWAP`), not
  appended at the end of the top row as first implemented — moved there afterward at the user's
  request, no functional difference.
- One follow-up, out of this plan's original scope: adding the WAKE key made CLASSIC's top row (5
  keys) outnumber the bottom layout-preset row (4), so a **V_WIDE_SPLIT_R** preset was added
  to balance it — the existing **V_WIDE_SPLIT** was relabeled **V_WIDE_SPLIT_L** for clarity. This
  is unrelated to wake-locking itself; see `man/layouts.md` and `src/web/shell/classic/mfd.js`'s
  `layoutIcons`/`applySplitClasses`/`setSplit` for that addition rather than this doc, which covers
  only the wake-lock feature.
- The "Open decisions for the implementing agent" section below is resolved: `noxmfd.wakelock` was
  the actual key name used; the CLASSIC failure banner extends `#mfd-indicators` (option 1); the
  F-35 banner sits ahead of the buttons, not crowding the avionics flags at any tested width.
- Not verified: a real LAN-connected tablet/phone on plain HTTP actually staying awake past its own
  display timeout — the one acceptance criterion nothing in this sandbox's browser pane could
  establish (it never reports `visibilityState: 'visible'`). Do this once picked up in-game.

## Origin

[Issue #53](https://github.com/roke77/NOXMFD/issues/53) — a device running NO XMFD as a persistent
cockpit display (tablet, phone, second monitor) can dim or lock its screen on its own OS-level
inactivity timeout, even while telemetry keeps updating. There is no way today to tell the browser
to keep the display awake.

A non-owner contributor, Genide, proposed a fix and posted a working implementation on their own
fork: `github.com/Genide/NOXMFD` branch `feature/screen-wake-lock` (single commit `b46e526`,
against a `main` commit from well before this repo's TGP manual-mode work landed — see
`docs/tgp-manual-control.md` / `docs/tgp-extended-quality.md` for that unrelated history). That
fork is **not** being merged. This plan is for an independent implementation, built by an agent
that treats the fork as reference material only — reusing the ideas that hold up, discarding
anything that doesn't fit this repo's actual conventions.

### What to take from the fork

- The overall shape is sound: a DOM-free, framework-free controller object exposing
  `start/stop/enable/disable/toggle/enabled/active`, constructed with injected `document`,
  `storage`, and `wakeLock` so it's unit-testable under plain `node`.
- The native-lock → canvas/video fallback strategy, in that order, is the right approach for the
  "served over plain HTTP, not a secure context" problem the ticket describes.
- A generation/operation counter guarding against a stale acquire/release race when the toggle
  fires again before the previous request settles.
- One shared module wired into both shells via its own `<script>` tag, rather than duplicating the
  logic per shell — this repo's established pattern (see `src/web/shell/shared/tgp-marks.js` and its
  `tgp-marks.test.js`, `src/web/shell/shared/nav-model.js`).

### What not to take

- **Do not copy files verbatim.** The fork's `f35.html` was run through an HTML auto-formatter,
  reindenting the entire file for a one-button change — a real diff-hygiene problem, not a design
  one, but reason enough to hand-write the integration against current `main` rather than port the
  file.
- The fork predates 67 commits of unrelated work on `main` (TGP manual control, extended
  resolution/quality, the `rates` page, etc.) and its raw diff against current `main` looks like it
  deletes huge amounts of code that it simply never had. None of that is relevant — build against
  current `main`, not against the fork's base.
- The fork's failure-indicator CSS (`.mfd-indicator.error` with a hardcoded red border) and its
  `.key.icon.on` amber-glow styling are reasonable starting points but were written without
  reference to this repo's existing conventions for the same states — see the CLASSIC integration
  section below for the actual established idioms to reuse instead.

## Player-facing spec (from the ticket)

- A wake-lock toggle button in both layouts, off by default:
  - CLASSIC: the 5th top function-key slot (alongside HIDE, FULL, PIN, SWAP).
  - F-35: beside the fullscreen button in the master strip.
- Visually distinct "on" state: amber border, icon, and glow — consistent between both shells.
- Preference persists per-browser (survives navigation and reload).
- While enabled:
  - Wake lock is held while the document is visible.
  - Released when the tab/window is hidden; the *preference* stays on.
  - Reacquired automatically when the document becomes visible again.
- Disabling releases any active lock immediately and persists the off preference.
- If both native acquisition and the fallback fail:
  - Preference turns itself off.
  - `WAKE LOCK FAILED` is shown for 5 seconds.
  - The original error(s) are logged to the console.
  - The active MFD page is never covered or interrupted.
- Works on a LAN-served, non-HTTPS connection (the plugin's actual deployment — see
  `SECURITY.md` / `docs/server-hardening.md`), where the native
  [Screen Wake Lock API](https://developer.mozilla.org/en-US/docs/Web/API/Screen_Wake_Lock_API) may
  be unavailable or reject as an insecure context — hence the muted inline `<video>` fallback,
  which works without HTTPS or any server-side change.
- No server, plugin (`src/plugin/`), or telemetry-protocol involvement at all: this is entirely
  browser-side UI state. Nothing in `TelemetryServer.cs`, `RatesConfig.cs`, or the SSE/telemetry
  JSON contract should change for this feature.

## Design

### Shared controller module

Add `src/web/shell/shared/wake-lock.js`, a DOM-free IIFE module in this repo's standard shape (compare
`src/web/shell/shared/tgp-marks.js`, `src/web/shell/shared/nav-model.js`): exports via `module.exports` under
Node and `root.WakeLock` (pick a name; the fork used `ScreenWakeLock`) in the browser, paired with
`src/web/shell/wake-lock.test.js` — a plain `assert`-based self-check runnable via
`node wake-lock.test.js`, no framework, per `docs/csharp-unit-testing.md`'s testing philosophy
(that doc is C#-specific but the "pure logic, DOM-free, one self-check file" principle is the same
one this repo's JS side already follows).

Constructor takes injected dependencies so the whole thing runs under `node` with fakes — no real
`document`, `localStorage`, or `navigator.wakeLock` required for the test:

- `document` — for `visibilityState` and the `visibilitychange` listener.
- `storage` — for persisting the on/off preference (real usage: `localStorage`).
- `wakeLock` — real usage: `navigator.wakeLock`; absent or lacking `.request` means the native API
  isn't available and the fallback path is used from the start.
- A fallback factory (creates the hidden muted-video approach) — injectable so the test suite can
  substitute a fake and assert the fallback path specifically, without a real `<canvas>`/`<video>`.
- `onState(state)` — called whenever enabled/active/mode changes, so each shell's button/icon
  re-renders from one callback instead of the controller reaching into shell DOM directly.
- `onError(error)` — called on final failure (both native and fallback exhausted), so each shell's
  own failure-banner UI can react without the controller knowing about `WAKE LOCK FAILED` text or
  DOM at all.

Responsibilities:

- `enable()` / `disable()` / `toggle()` — the persisted intent, independent of whether an actual
  lock is currently held.
- Persist the intent to `storage` under a single key immediately on every change (before the async
  acquire/release even resolves), so a preference is never lost by a page reload racing acquisition.
- `start()` — reads the persisted preference, attaches the `visibilitychange` listener, and
  attempts to acquire if the preference was on. Call once per shell at boot, after DOM is ready.
- Guard every acquire/release path with a monotonic operation token so a `toggle()` that fires
  again before the previous async operation settles cannot end up holding a lock nobody asked for,
  or leaking one nobody released — this is the one piece of real concurrency risk in an otherwise
  simple feature, and the fork's generation-counter approach is a reasonable model to build from.
- Native-lock request rejecting (insecure context, permission denial, etc.) falls through to the
  fallback automatically, without the caller needing to distinguish the two paths.
- Visibility change while enabled: release on hidden, reacquire on visible — but only the *lock*,
  never the persisted preference.

### Native lock vs. fallback

`navigator.wakeLock.request('screen')` is the real API and should be tried first whenever
`navigator.wakeLock` exists. On a plain-HTTP LAN address this either doesn't exist at all in some
browsers or the request rejects — that failure must be caught and treated as "fall back," not as a
final failure.

The fallback: a 1x1 (or similarly tiny) `<canvas>`, redrawn on an interval so
`canvas.captureStream()` has a live stream, fed into a hidden (`opacity` near 0, `pointer-events:
none`, off-screen), `muted`, `playsInline` `<video>`. A muted, playing, visible-enough video is
historically the one reliable no-permission way to keep mobile Chrome/Safari from dimming the
screen without HTTPS. `canvas.captureStream` missing means the browser supports neither approach —
that's the real "both methods failed" case the ticket's failure-handling section describes.

### Persistence

One `localStorage` key (namespaced, e.g. `noxmfd.wakelock`) holding `'true'`/`'false'`. Wrap every
read/write in try/catch — this repo already does this everywhere `localStorage` is touched (e.g.
`setLayout()` in `mfd.js`) because private-mode browsers can throw on access; a failed write should
just mean the preference isn't sticky, not an uncaught exception.

### Failure UI

`onState`/`onError` deliberately keep the controller ignorant of specific DOM or wording, so each
shell renders its own banner. Suggested shape for both shells: a short-lived text element next to
the button, shown for exactly 5 seconds on `onError`, cleared early if the user disables the toggle
themselves in the meantime. This does not need to be a generic new toast subsystem — a single
dedicated element is enough, matching how the F-35 shell's master strip already has room for a
narrow status element next to its buttons.

For CLASSIC specifically: `renderIndicators()` / `indicatorOrder` (`mfd.js` around line 1170-1278)
is the existing top-right corner stack, but it is purpose-built for *named, persistent* states
(currently only `pinned`) looked up via `indicatorVisible()` — it is not a generic toast queue.
Two reasonable options, left to the implementing agent to pick based on how it reads once wired up:

1. Extend that stack with one more transient, self-timing entry (closest to what the fork did),
   accepting that it slightly stretches the stack's "named persistent indicator" contract.
2. Add a small dedicated element near the bezel's top-key bank instead, independent of
   `#mfd-indicators`, avoiding that stretch entirely.

Either way: never cover or replace the active page content — the ticket is explicit about this.

## CLASSIC shell integration

`src/web/shell/classic/mfd.js`:

- `COUNTS['keys-top']` goes from `4` to `5` (line 1) to seat the new key — this alone reflows the
  bezel's existing ridge/key/ridge generation loop, no other change needed there.
- Append one entry to `functionIcons` (line ~35-40): `{ cls: 'ic-wake', title: 'Keep screen
  awake', action: 'wake' }`.
- Instantiate the controller once at boot (near where other shell-level state is set up), with
  `onState` toggling a new "lit" class on the wake key and updating its title between "Keep screen
  awake" / "Allow screen sleep", and `onError` driving whichever failure-banner approach was
  chosen above.
- Add a `case 'wake':` arm to the `mfdButton()` switch (near the existing `'fll'`/`'pin'`/`'swap'`
  cases around line 2200-2254) that calls `wakeController.toggle()`.
- Call `wakeController.start()` once, alongside the existing shell boot sequence (near
  `loadConfigUrls(); showPage('main');` at the end of the file).

`src/web/shell/classic/mfd.html`: add `<script src="/assets/shell/shared/wake-lock.js"></script>` before
the `mfd.js` script tag (same ordering the other shared shell modules use).

`src/web/shell/classic/mfd.css`: this repo already has an established "engaged" amber idiom —
`.overlay-item.on` (around line 554) boxes a page-name label in amber with an outline when it names
an active choice. The wake key is a *physical bezel key* carrying persistent on/off state, which no
existing key currently does (PIN's state shows on the corner PINNED chip, not on the key itself),
so this is a legitimately new small CSS rule, not a duplicate of something that already exists —
add a `.key.icon.on` (or similarly scoped) rule using `var(--no-amber)` for border/icon color plus
a subtle glow, consistent in spirit with `.overlay-item.on`'s amber engaged treatment rather than
inventing a new color language.

## F-35 shell integration

`src/web/shell/f35/f35.html`: add a wake button as a sibling of the existing `#ms-fll` button
inside `.master-strip` (around line 128), e.g. `<button type="button" class="nav-item ms-wake"
id="ms-wake" title="Keep screen awake" aria-pressed="false"><span class="ms-wake-icon"
aria-hidden="true"></span></button>`, placed immediately before or after `#ms-fll` per the ticket's
"beside the fullscreen button." Add the failure-banner element as its sibling if using a dedicated
element (see Failure UI above). Add the `wake-lock.js` `<script>` tag before `f35.js`'s, matching
the CLASSIC placement.

**Make this edit directly against current `main`'s `f35.html`, not by porting any part of the
fork's copy** — the fork's version of this file was reformatted wholesale and is not a usable diff
source.

`src/web/shell/f35/f35.css`: `.nav-item.on` (line 646) already gives a button the amber-box
"engaged" treatment used elsewhere in this shell (e.g. layout picker, per the comment at line 397)
— toggling the `on` class from `onState` is enough; no new color rule should be needed here, unlike
CLASSIC. Add an icon rule mirroring `.ms-fll-icon` (line 316) for `.ms-wake-icon`, masked with a new
icon token (see below) instead of `--icon-fullscreen`.

`src/web/shell/f35/f35.js`: construct the controller the same way as the CLASSIC shell (same
`onState`/`onError` shape, different DOM targets), wire the button's `click` to `.toggle()`, and
call `.start()` once during the shell's existing boot sequence.

## Icon

Add one new mask token to `src/web/shared/theme.css` alongside the existing `--icon-fullscreen`
(line 51), e.g. `--icon-wake`, following the same "one shared SVG data-URI token, `currentColor`
via `background-color` + mask" pattern both shells already use for the fullscreen glyph. A sun (or
sun-with-rays) glyph matches the ticket's screenshots and reads clearly at the 16px size both
shells use for their function icons.

## Manuals and docs

- `man/layouts.md` — this is the manual for shell-chrome controls, not a specific MFD page (compare
  its existing CLASSIC "Function controls (top)" list: HIDE, FULL, PIN, SWAP). Add the wake-lock
  toggle to that list, and add one line to the F-35 section describing the master-strip button.
  Do not document it under any MFD page's own manual (`man/hud.md`, etc.) — it isn't configured
  from a CFG page and has no per-page relevance.
- `src/web/README.md` — add `wake-lock.js` to the `shell/` file inventory (same style as the
  existing `nav-model.js`/`ext-nav.js`/`tgp-marks.js` rows).
- `README.md` — optional one-clause mention under Features if it reads naturally; this is a shell
  feature rather than a page, so it doesn't need its own bullet under "MFD pages."
- No changes needed to `docs/csharp-unit-testing.md` (no C# involved), `SECURITY.md` (no new
  Harmony patch or server behavior), or any `docs/tgp-*.md` file — none of this touches that
  surface area.

## Test plan

`src/web/shell/wake-lock.test.js`, plain `assert`, run via `node wake-lock.test.js` (matching every
other `*.test.js` self-check in this repo — no framework, no fixtures). At minimum, cover:

- Starting with no persisted preference leaves the lock off and does not call `wakeLock.request`.
- Starting with a persisted "on" preference immediately attempts to acquire.
- `toggle()` from off calls `wakeLock.request('screen')` with a `'screen'` type argument, and
  persists the new preference before the request resolves.
- A native request that rejects falls through to the injected fallback factory, and `onState`
  ends up reporting the lock as active via the fallback.
- Fallback failure after native failure surfaces to `onError` exactly once, and the preference is
  flipped back to persisted-off as a result (per the ticket's failure-handling requirement).
- Disabling while an acquire is still in flight does not leave a lock held once that in-flight
  operation settles (the concurrency case the generation/operation counter exists for).
- A `visibilitychange` to hidden while enabled releases the active lock but leaves the persisted
  preference untouched; a subsequent change back to visible reacquires without needing another
  `toggle()` call.
- `storage.setItem`/`getItem` throwing (private-mode simulation) doesn't throw out of the
  controller.

## Acceptance criteria

Mirrors the ticket's own checklist:

- [x] CLASSIC's WAKE key (top row, between FULL and PIN) and F-35's master-strip button both toggle
      the same shared controller logic, independently instantiated per shell.
- [x] Both controls are real `<button>` elements with a `title`/`aria-pressed` pair that updates
      live, so they work by touch, mouse, and assistive technology without extra ARIA plumbing.
- [x] The "on" visual treatment reads the same way conceptually in both shells (amber), even though
      each shell's existing CSS idiom for "engaged" differs slightly (`.key.icon.on` vs.
      `.nav-item.on`).
- [x] Preference defaults off, persists across reload/navigation, survives a private-mode
      `localStorage` failure without throwing.
- [x] Lock releases on `visibilitychange` to hidden and reacquires on visible, without touching the
      persisted preference either way.
- [x] Toggling off during an in-flight acquire cannot leave a stale native or fallback lock active.
- [x] Native rejection automatically attempts the video fallback with no user-visible gap beyond
      normal latency.
- [x] Both-methods-failed shows `WAKE LOCK FAILED` for 5 seconds, logs the underlying error(s) to
      console, turns the preference off, and never covers the active MFD page.
- [x] `node wake-lock.test.js` passes; full JS suite (`tools/serve_web.py`'s smoke check plus every
      other `*.test.js`) is unaffected.
- [ ] Manual, real-device check: a LAN-connected tablet/phone (plain HTTP, non-secure context)
      actually stays awake past its own configured display timeout — this is the one criterion
      nothing in the automated suite can establish; still open, per the Status note above.

## Likely files involved

- `src/web/shell/shared/wake-lock.js` (new) — the shared controller + fallback factory.
- `src/web/shell/wake-lock.test.js` (new) — self-check.
- `src/web/shell/classic/mfd.js` — `COUNTS`, `functionIcons`, `mfdButton()` case, boot wiring,
  failure-banner state.
- `src/web/shell/classic/mfd.html` — new `<script>` tag.
- `src/web/shell/classic/mfd.css` — new key icon, new `.on` state rule.
- `src/web/shell/f35/f35.html` — new button + (optional) banner element, new `<script>` tag.
- `src/web/shell/f35/f35.js` — controller instantiation, click wiring, boot call.
- `src/web/shell/f35/f35.css` — new icon rule.
- `src/web/shared/theme.css` — new `--icon-wake` token.
- `man/layouts.md` — CLASSIC/F-35 control lists.
- `src/web/README.md` — file inventory.

## Open decisions — resolved

1. **Failure-banner mechanism**: option 1 — CLASSIC's `#mfd-indicators` stack gained a transient
   `.mfd-indicator.error` entry (`wakeLockError` state + its own timer in `mfd.js`), rather than a
   separate dedicated element.
2. **`localStorage` key**: `noxmfd.wakelock`, as guessed — `WakeLock.STORAGE_KEY` in
   `wake-lock.js`.
3. **Sun icon**: a simple circle + eight rays, `--icon-wake` in `theme.css`, masked the same way as
   `--icon-fullscreen`.
4. **F-35 banner placement**: `#ms-wake-error` sits as a strip-level sibling ahead of the buttons
   (`ms-wake`, then `ms-fll`) — verified it doesn't crowd the avionics flags in the browser preview.

## References

- [Issue #53](https://github.com/roke77/NOXMFD/issues/53) — the original request and full
  acceptance criteria.
- `github.com/Genide/NOXMFD` branch `feature/screen-wake-lock` — community reference
  implementation; consult for approach, do not port files directly (see "Origin" above).
- [MDN: Screen Wake Lock API](https://developer.mozilla.org/en-US/docs/Web/API/Screen_Wake_Lock_API)
- `src/web/shell/shared/tgp-marks.js` / `tgp-marks.test.js` — the shared-pure-module + self-check pattern
  this plan follows.
- `man/layouts.md` — where the new control gets documented.
- `docs/tgp-extended-quality.md` — an example of this repo's planning-doc format and of a
  live-device-validation checklist for a criterion automated tests can't cover.
