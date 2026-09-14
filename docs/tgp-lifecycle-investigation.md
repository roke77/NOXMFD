# TGP / CFG navigation lifecycle

This experiment extends the MAP lifecycle work on `map-lifecycle-experiment`.
TGP / CFG navigation replaces the iframe document, recreating the image consumer.
TGP already removes its image source and cancels retries on pagehide; missing
observer and active joystick cleanup are concrete gaps, not proof of a RAM leak.

## Implemented cleanup

- Idempotent pagehide disconnects the observer, removes the stream source, clears
  retry and active joystick work, and empties target boxes.
- Pointer capture loss and window blur release joystick input.
- Late messages and resize callbacks do not render after teardown.
- Persisted pageshow reconnects the image and observer without resuming input.
- Node tests cover teardown, input release, idempotence, and restoration.
- `tools/tgp-lifecycle-browser.cjs` exercises 30 classic TGP / CFG transitions
  against the preview and checks teardown/restoration in a real browser.

## Required live validation

The preview serves a static image, not multipart MJPEG. Passing preview tests
does not establish that browser memory stabilizes with a live feed.

- [ ] Keep resolution/rate/settings fixed; record browser process memory and JS
  heap before and after 30 TGP / CFG cycles, then after an idle period.
- [ ] Repeat with a static image control and compare post-GC heap snapshots for
  retained TGP documents; distinguish JS retention from process/image memory.
- [ ] Repeat on classic split and F-35 layouts, including multiple visible TGPs.
- [ ] Navigate while dragging the joystick; camera input must stop promptly.
- [ ] Verify live requests return to the number of visible TGPs, including when
  frames stop arriving. Server disconnect detection can depend on a later write.
- [ ] Test retry after disconnect and browser back/forward restoration.

## Shared TGP / CFG document experiment

All layout tables route CFG to the TGP document's `#cfg` view. The shared frame
navigator changes only the fragment while this pair is mounted; it does not assign
a new iframe source on return. Configuration markup is imported once from the
standalone `/tgpcfg` page, and its scoped controller initializes once. Settings
refresh on each CFG entry. No nested iframe or duplicate keybind listener is created.

CFG suspends the image source, observer, retries, and joystick; returning resumes
the existing TGP elements. Leaving the pair disposes it, including CFG-to-MAP in
classic full view. Standalone `/tgpcfg` remains available.

Browser checks assert document identity across 30 full-view and five split-view
round trips and a single configuration-markup request. The live allocation-spike
comparison remains an in-game check; this removes repeated document creation but
does not claim to eliminate MJPEG decoder allocations.
