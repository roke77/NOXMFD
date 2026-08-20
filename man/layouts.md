# LYT

NO XMFD can render in more than one shell layout — a different frame, navigation, and split model
over the same pages. Two are supported for now: **CLASSIC** and **F-35**.

## CLASSIC

The metallic bezel layout. A single active page fills the screen, or splits into two panes,
framed by dedicated bezel buttons — function controls along the top, layout presets along the
bottom.

**Function controls (top):**

- **HIDE** — hide the bezel so the screen fills the viewport.
- **FULL** — fullscreen toggle.
- **PIN** — pin a page.
- **SWAP** — jump to/from the pinned page.

**Layout presets (bottom):**

- **F_VIEW** — single page, full screen.
- **H_SPLIT** — top/bottom split.
- **V_SPLIT** — left/right split.
- **V_WIDE_SPLIT** — left/right split, 2:1.

![V_SPLIT (left) and H_SPLIT (right)](../docs/images/H_V_SPLIT.png)

![V_WIDE_SPLIT](../docs/images/V_WIDE_SPLIT.png)

## F-35

A borderless, touch-driven layout modelled on the real F-35's panoramic cockpit display: there
are no bezel keys — the navigation labels are drawn on the glass and tapped directly, and the
screen divides into side-by-side portals, each an independent MFD, that you merge and split with
corner grips. A fixed strip across the top carries the aircraft-level readouts — connection,
throttle and fuel, and the avionics flags.

![F-35 layout — MAIN](../docs/images/F-35%20MAIN.png)

![F-35 layout — 1-2-1 portal split](../docs/images/F-35%201-2-1.png)

![F-35 layout — 2-2 portal split](../docs/images/F-35%202-2.png)
