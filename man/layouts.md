# LYT

NO XMFD can render in more than one shell layout — a different frame, navigation, and split model
over the same pages. Reached from MAIN via **CFG** in either layout, alongside [HUD](hud.md),
[KEY](keybinds.md), and [RTS](rates.md). Two are supported for now: **CLASSIC** and **F-35**.

## CLASSIC

The metallic bezel layout. A single active page fills the screen (or splits into two panes), and
dedicated bezel buttons frame it — function controls along the top, layout presets along the
bottom.

**MFD shell** — the bezel chrome around the active page:

- **HIDE** — hide the bezel so the screen fills the viewport.
- **FULL** — fullscreen toggle.
- **PIN** — pin a page.
- **SWAP** — jump to/from pin.
- **F_VIEW** — single page.
- **H_SPLIT** — top/bottom split.
- **V_SPLIT** — left/right split.
- **V_WIDE_SPLIT** — left/right 2:1 split.

<details>
<summary>$\color{green}\textsf{See screenshots}$</summary>

![V_SPLIT (left) and H_SPLIT (right)](../docs/images/H_V_SPLIT.png)

![V_WIDE_SPLIT](../docs/images/V_WIDE_SPLIT.png)

</details>

## F-35

A borderless, touch-driven layout modelled on the real F-35's panoramic cockpit display: there
are no bezel keys — the navigation labels are drawn on the glass and tapped directly, and the
screen divides into side-by-side portals, each an independent MFD, that you merge and split with
corner grips. A fixed strip across the top carries the aircraft-level readouts — connection,
throttle and fuel, and the avionics flags.

<details>
<summary>$\color{green}\textsf{See screenshots}$</summary>

![F-35 layout — MAIN](../docs/images/F-35%20MAIN.png)

![F-35 layout — 1-2-1 portal split](../docs/images/F-35%201-2-1.png)

![F-35 layout — 2-2 portal split](../docs/images/F-35%202-2.png)

</details>
