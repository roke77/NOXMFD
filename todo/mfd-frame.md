# MFD-style page (bezel frame around the map)

## Goal
Add a new web page styled as a rugged **Multi-Function Display (MFD)**: a hardware-gray bezel
with rows of buttons on all four sides plus corner controls, with the **existing map component
shown in the central screen**. Buttons are clickable but do nothing yet (no-op with a pressed
state) — the wiring of real actions is a later task.

This is a **new, separate page**; the current HUD at `/` is unchanged.

## Decisions
- **Central screen:** just the map (no header / no HUD sidebar inside the frame).
- **Bezel:** match the reference MFD — buttons along all four sides + corner controls
  (power, brightness `-☀+`, grid toggle), all clickable no-ops with a pressed/lit state.
- **Look:** the **bezel is hardware-gray**; the **screen inside keeps the green HUD theme**
  (`#39ff14` on `#060a06`). Courier New throughout, consistent with the rest of the UI.

## Approach

### 1. New route `/mfd`
- New static class `src/MfdPage.cs` holding the page as a raw HTML/CSS/JS string — same
  convention as `ClientPage.Html`.
- In `src/TelemetryServer.cs`, in the accept loop's path switch (alongside `/stream`, `/map`,
  `/icon`, `/weapon`), add:
  ```csharp
  else if (path == "/mfd") ServeMfd(ctx);
  ```
  and a `ServeMfd(ctx)` method that writes `MfdPage.Html` with `text/html; charset=utf-8`
  (copy of the existing `ServeHtml`).

### 2. Reuse the map via an iframe (no duplication of map logic)
- The MFD's central screen contains `<iframe src="/?bare">`, so all the map logic in
  `ClientPage.cs` (canvas, SSE, zoom/pan/follow, hover labels, icons) is reused unchanged.
- Add a small **"bare" mode** to `ClientPage.cs` — the only edit to existing code, additive:
  - On load, if `location.search` includes `bare`, add a `bare` class to `<body>`.
  - CSS: `body.bare > header, body.bare #hud { display: none; }` so `#map-panel` (which is
    already `flex: 1`) fills the whole frame → "just the map."
- Result: `/` is the full HUD as today; `/?bare` is the map only; `/mfd` is the map-only view
  framed by the bezel.

### 3. Bezel layout (pure CSS, no image assets)
- Outer frame as a 3-row CSS grid:
  1. top button strip
  2. `[ left buttons · screen · right buttons ]`
  3. bottom button strip
- Gunmetal look from layered `linear-gradient` backgrounds + inset `box-shadow`, rounded
  corners, beveled keys (light top edge / dark bottom edge highlights).
- Buttons generated from JS arrays so counts/labels are easy to tune. Start approximating the
  reference: ~10 top, ~10 bottom, ~6 left, ~6 right. Each key is a small beveled rectangle
  with an optional label.
- **Corner controls:** top-left power (⏻) + a display toggle; top-right brightness `-☀+` and
  a 2×2 grid icon. Rendered as special styled keys (no-op for now).
- Central screen: an inset dark recess (subtle bevel) holding the iframe.

### 4. Button behavior (no-op but responsive)
- One shared handler `mfdButton(id)` that does nothing functional — applies `:active` plus a
  brief "lit" class so a press feels real. Leave a clear `// TODO: wire action` seam.

### 5. Theming
- Bezel grays e.g. `#2a2d31` / `#3a3e44` / `#1c1e21` with light edge highlights.
- Inner screen unchanged (green HUD), since it's the existing page in the iframe.

### 6. Responsiveness
- Frame scales to the viewport (`100vh`, grid/flex); the screen flexes; body never scrolls.

## Preview integration
`tools/build_preview.py` extracts `ClientPage.Html` → `preview/index.html` and injects
`tools/preview-mock.js` (mocks `/stream`, `/map`, `/icon`, `/weapon`) so the UI runs over
`file://` with no game. The MFD page should be previewable the same way:
- Extend `build_preview.py` to also emit `preview/mfd.html` from `MfdPage.Html`.
- Because `/?bare` won't resolve over `file://`, point the preview's iframe at the generated
  bare preview file instead (e.g. emit a `preview/index-bare.html` and have the MFD preview
  iframe it). Keep this in the build task, not the page itself.

## Verification (when built)
- `dotnet build -c Release` compiles (new `MfdPage.cs` + route).
- Open `http://localhost:5005/mfd`: bezel renders, the map fills the central screen, every
  button is clickable with a pressed state, no console errors.
- Confirm `/` is unchanged and `/?bare` shows the map with no header/sidebar.
- `python tools/build_preview.py` produces a previewable MFD page.

## Out of scope (separate future todos)
- Wiring real button functions.
- The 2×2 multi-panel grid + grid/single-view toggle (like the reference's quad view).
- Making the MFD the default landing page.
