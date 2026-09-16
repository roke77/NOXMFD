# MAP lifecycle experiment

Branch: `map-lifecycle-experiment`. This experiment separates background telemetry needs from
MAP rendering lifetime. It does not establish the cause of reported cumulative browser RAM growth.

## Implemented boundary

- F-35's permanent `/map-view?telemetry=1` iframe loads a transport-only document. It has no
  MAP renderer, map image, canvas, icon cache, route store, or remote keyboard module.
- Classic full view retains its canonical MAP transport, but the shell explicitly activates its
  renderer only when MAP is selected outside split mode. Hidden rendering and interaction stop.
- MAP cancellation covers rendering animation frames, flash animations, missile timers, retry
  timers, pointer long presses, and the PAD cursor. Activation renders the latest retained frame.
- `pagehide` additionally closes transport, disconnects the resize observer, removes image sources
  and callbacks, zeroes canvas backing stores, and clears render caches. A BFCache restoration
  reloads the disposed document. Resizing assigns canvas dimensions only when they change.
- Visible MAP panes used to own independent telemetry sources; see
  `docs/mfd-shared-telemetry-connection.md` — they now render from the shell's relayed frames
  instead of opening their own connection.

## Verification

`tools/map-lifecycle-browser.cjs` is an opt-in Playwright check against the running preview at 8782.
It checks repeated classic MAP/WPT navigation, no hidden MAP drawing, MAP document reuse,
F-35 telemetry delivery without map resources, and repeat-safe disposal. Set `NODE_PATH` to a
Playwright installation and run `node tools/map-lifecycle-browser.cjs` (uses installed Edge).

Live checks still required:

- [ ] Classic full: repeat MAP/WPT transitions with real contacts and route data; check SOI/PAD
  focus, pan, zoom, follow, grid and route changes while MAP is hidden.
- [ ] Classic split/F-35: navigate MAP/WPT repeatedly in each pane; verify other pages retain
  telemetry, target selection works, and stream counts return to the expected visible MAP count
  plus the permanent transport.
- [ ] Exit and enter a mission while MAP is hidden; confirm the new map and routes appear.
- [ ] Compare renderer/GPU memory after warm-up and 50 navigation cycles against main. The
  automated checks establish resource behavior, not a browser-memory plateau.
