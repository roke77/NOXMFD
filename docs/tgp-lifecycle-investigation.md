# TGP / CFG navigation lifecycle

This experiment extends the MAP lifecycle work on `map-lifecycle-experiment`.
Status: implemented; the user reports a successful live test on 2026-09-14 after
the shared-document change. This is qualitative confirmation, not a measured
before/after memory benchmark or verification of every layout and edge case.

## Report and initial architecture

Repeated TGP → CFG → TGP navigation increased Brave's displayed memory substantially
at `http://localhost:5005/`. MAP/WPT lifecycle changes had already produced positive
results, motivating the same investigation for TGP.

Before this change, all three routing paths replaced the iframe document:
classic full view (`showFramePage`), classic split (`paneNavigate`), and F-35 portal
(`showPage`). Each round trip recreated TGP's DOM, scripts, input handlers,
ResizeObserver and MJPEG image consumer, plus CFG's document, scripts and settings
initialization. Browser caching did not make these document instances reusable.

TGP already removed its image source and cancelled retries on pagehide. CFG fetched
`/rates-config` once per load and had no recurring polling or media stream. The
problem was not simply a missing image teardown or an expensive CFG polling loop.

## Implemented cleanup

Commit `e97028e` completes resource cleanup but does not remove document recreation.
The user still observed memory growth during navigation with this build.

- Idempotent pagehide disconnects the observer, removes the stream source, clears
  retry and active joystick work, and empties target boxes.
- Pointer capture loss and window blur release joystick input.
- Late messages and resize callbacks do not render after teardown.
- Persisted pageshow reconnects the image and observer without resuming input.
- Node tests cover teardown, input release, idempotence, and restoration.

## Live evidence and snapshot findings

- The live `/assets/pages/tgp/tgp.js` response contained the new observer and
  joystick cleanup, confirming the server served the updated code.
- The game log repeatedly reported `TGP: disengaged (no subscribers, encoderDrops=0)`
  between visits. This supports successful subscriber cleanup during the observed
  run, rather than an ever-growing list of live streams. It does not cover stalled
  writes or every disconnect scenario.
- The displayed counter was JavaScript heap, not total browser process RAM.
  The user reported a drop from approximately 570 MB to 117 MB when taking a
  snapshot; a screenshot also showed a pre-collection reading of 746 MB.
- Heap snapshots trigger garbage collection. A large drop therefore demonstrates
  reclaimable allocations, not that navigation is inexpensive.

The three supplied snapshots were parsed directly; sizes below are sums of node
`self_size` in decimal MB, not dominator retained sizes or process working sets.

| Snapshot filename suffix | Heap size | Heap nodes | Native HTMLDocument nodes | `syncOverlayRect` closures |
| --- | ---: | ---: | ---: | ---: |
| `162406` | 115.47 MB | 2,137,977 | 10 | 2 |
| `162414` | 116.70 MB | 2,097,414 | 10 | 1 |
| `162418` | 116.81 MB | 2,089,853 | 10 | 2 |

Post-collection size grew about 1.34 MB while total node counts decreased. Document
counts stayed stable and TGP closures fluctuated rather than accumulating. These
samples do not establish a large permanent TGP-document leak, nor exclude smaller
retention problems or native allocations outside the captured heap. The snapshots
also contained extension execution contexts, so the complete heap cannot be
attributed exclusively to NOXMFD. No extension was established as the cause.

The investigation consequently focuses on avoiding repeated allocations, not merely
proving that garbage collection eventually recovers them. More snapshots or an
extension-isolation detour did not address the user's actual navigation complaint.

## Validation and remaining coverage

The preview serves a static image, not multipart MJPEG. Passing preview tests
does not establish that browser memory stabilizes with a live feed.

- [x] User live test of the shared-document fix: reported “that worked nice.”
- [x] Browser preview: stable document identity over 30 classic full-view and five
  split-view round trips; configuration markup requested only once in the full-view run.
- [x] Full CI: Release build, 48 JavaScript test files, 370 C# tests, route smoke.
- [x] Unit/browser checks for teardown, input cleanup and persisted restoration.
- [ ] Optional quantitative follow-up: fixed-settings before/after allocation and
  process-memory measurements, comparing navigation with an equal-duration idle run.
- [ ] Repeat on classic split and F-35 layouts, including multiple visible TGPs.
- [ ] Navigate while dragging the joystick; camera input must stop promptly.
- [ ] Verify live requests return to the number of visible TGPs, including when
  frames stop arriving. Server disconnect detection can depend on a later write.
- [ ] Test retry after disconnect and browser back/forward restoration.

## Shared TGP / CFG document experiment

Commit `8e1abe9` addresses the repeated creation directly. The user confirmed a
positive result after testing this change in game.

All layout tables route CFG to the TGP document's `#cfg` view. The shared frame
navigator changes only the fragment while this pair is mounted; it does not assign
a new iframe source on return. Configuration markup is imported once from the
standalone `/tgpcfg` page, and its scoped controller initializes once. Settings
refresh on each CFG entry. No nested iframe or duplicate keybind listener is created.

CFG suspends the image source, observer, retries, and joystick; returning resumes
the existing TGP elements. Leaving the pair disposes it, including CFG-to-MAP in
classic full view. Standalone `/tgpcfg` remains available.

The shared navigator changes `contentWindow.location.hash` for the mounted pair.
Browser tests caught document recreation when returning via a full-URL replacement;
explicit fragment assignment preserves identity in the tested round trips. The
server-route test strips fragments because they are not sent as HTTP paths.

This is bounded reuse of one related pair per display surface, not a cache of every
visited page. It avoids nested CFG iframes, duplicate keybind initialization and an
invisible running feed. MJPEG reconnects on return, so decoder allocation cost is
not claimed to be eliminated. Overlay target-box rebuilding and geometry updates
remain possible separate rendering optimizations; they were not changed or proven
to explain the navigation spikes.

## Deployment and scope

Changes stay on `map-lifecycle-experiment`; no merge to main is implied. The build
configuration deploys to the isolated staging BepInEx folder, not the running Steam
installation. A live test needs the staged DLL installed and the game restarted;
browser refresh alone cannot replace the plugin's embedded assets. Snapshot files
are diagnostic inputs and are not committed to the repository.
