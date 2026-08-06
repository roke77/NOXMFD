// The PAD cursor (docs/page-cursor.md, docs/map-cursor.md) — a crosshair standing in for the
// mouse/touch while a page is the HOTAS-driven SOI's focused surface. Extracted from map.js (the
// first page to have one) so TGT/HUD can reuse the exact same feel: a velocity vector integrated
// locally at ~60 Hz between the ~10 Hz transport frames, clamped to a page-supplied rect, plus a
// discrete Select edge. This module owns the crosshair element, its position, and the integrator;
// it knows nothing about what's under the cursor — that's the caller's onSelect/onHold/onMove.
//
// ponytail: SPEED is a flat constant tuned by feel (crosses a typical panel in ~1.5-2s at full
// deflection), same as map.js's original CURSOR_SPEED; a config entry can come later if anyone
// wants to tune it live instead.
const DEFAULT_SPEED = 700;   // px/second at full deflection
const DEFAULT_HOLD_MS = 500; // matches the DOM press/hold pages already use (e.g. tgt.js's LONG_MS)

// clampRect() returns { dx, dy, dw, dh } — the rect (in the same px space as the crosshair
// element's positioned ancestor) the cursor may not leave.
//
// onSelect(x, y)   — Cursor Select released before holdMs (or a page with no onHold at all, like
//                     MAP): the caller decides what "select" means (MAP's nearest-contact hit-test,
//                     a page's DOM elementFromPoint).
// onHold(x, y)     — optional: Cursor Select held past holdMs — a page's long-press equivalent
//                     (docs/page-cursor.md). Omit it and every press is a plain onSelect, exactly
//                     MAP's original behaviour (its instant edge-driven select, unaffected).
// onMove(x, y)     — optional: called with the cursor's current point every time it's visible and
//                     placed, and with (null, null) when it's hidden/unfocused — a page uses this to
//                     highlight whatever's under the crosshair (docs/page-cursor.md #2). Cheap to
//                     omit (MAP doesn't use it).
// onEdge(ex, ey, dt) — optional: called during the drive integration when the (pre-clamp) position
//                     has overshot clampRect by (ex, ey) px — a page can react (MAP pans its view
//                     under a screen-pinned cursor, docs/page-cursor.md #3). The cursor is clamped
//                     to the rect regardless, right after this fires.
export function createPadCursor({ el, clampRect, onSelect, onHold, onMove, onEdge, speed, holdMs }) {
  const SPEED = speed || DEFAULT_SPEED;
  const HOLD_MS = holdMs || DEFAULT_HOLD_MS;
  let on = false;            // is this surface the SOI's focused MAP/TGT/HUD right now?
  let pos = null;             // {x,y} in clampRect's px space; null when not focused or not yet placed
  let vec = { x: 0, y: 0 };   // last reported [-1,1] velocity, held between broadcasts
  let timer = null, lastT = 0;
  let holdTimer = null, holdFired = false;   // Select press/hold arbitration (see setSelectHeld)
  // Externally forced invisible (docs/tgt-keybind-nav.md) — a page can have its own second selection
  // mode (TGT's Next/Previous Target row-stepper) that's mutually exclusive with the free crosshair;
  // setHidden lets it suppress the crosshair without touching `on`/`pos`, so the crosshair picks up
  // exactly where it was left the moment it's un-hidden (same "parked, not forgotten" contract focus
  // loss already has). Unused by MAP/HUD — plain false forever, paint() behaves exactly as before.
  let hidden = false;

  function clamp() {
    if (!pos) return;
    const r = clampRect();
    pos.x = Math.max(r.dx, Math.min(r.dx + r.dw, pos.x));
    pos.y = Math.max(r.dy, Math.min(r.dy + r.dh, pos.y));
  }

  // Transform, not left/top, so it stays a compositor move and never reflows/repaints the page
  // underneath it.
  function paint() {
    if (!on || !pos || hidden) {
      el.style.display = 'none';
      if (onMove) onMove(null, null);
      return;
    }
    el.style.display = 'block';
    el.style.transform = 'translate(' + (pos.x - 12) + 'px,' + (pos.y - 12) + 'px)';
    if (onMove) onMove(pos.x, pos.y);
  }

  // Integrates the last-known velocity between broadcasts, uses performance.now() rather than the
  // rAF callback's own timestamp so it doesn't depend on how a given embedder invokes
  // requestAnimationFrame. Self-stops once the vector is zero; ensureAnim restarts it.
  function drive() {
    if (!on || !pos || (!vec.x && !vec.y)) { timer = null; lastT = 0; return; }
    const now = performance.now();
    const dt = lastT ? Math.min(0.1, (now - lastT) / 1000) : 0;
    lastT = now;
    pos.x += vec.x * SPEED * dt;
    pos.y += vec.y * SPEED * dt;
    if (onEdge) {
      const r = clampRect();
      const ex = pos.x < r.dx ? pos.x - r.dx : pos.x > r.dx + r.dw ? pos.x - (r.dx + r.dw) : 0;
      const ey = pos.y < r.dy ? pos.y - r.dy : pos.y > r.dy + r.dh ? pos.y - (r.dy + r.dh) : 0;
      if (ex || ey) onEdge(ex, ey, dt);
    }
    clamp();
    paint();
    timer = requestAnimationFrame(drive);
  }
  function ensureAnim() {
    if (on && pos && (vec.x || vec.y) && !timer) { lastT = 0; timer = requestAnimationFrame(drive); }
  }

  return {
    // Focus changed: center it the first time this surface is focused (predictable), then leave it
    // wherever the pilot parks it — losing focus hides it rather than forgetting its spot.
    setFocus(v, centerX, centerY) {
      on = v;
      if (on && !pos) pos = { x: centerX, y: centerY };
      if (!on) vec = { x: 0, y: 0 };
      paint();
    },
    setVector(x, y) { vec = { x: x || 0, y: y || 0 }; ensureAnim(); },
    // Plain edge-driven select — a press that never needs to be told from a hold (MAP; also the
    // default for any page that doesn't pass onHold).
    select() { if (on && pos && onSelect) onSelect(pos.x, pos.y); },
    // Cursor Select's LIVE held state (docs/page-cursor.md) — only meaningful when the page passed
    // onHold. Mirrors a real pointerdown/pointerup pair: rising edge arms a HOLD_MS timer: if it
    // fires while still held, that's the hold outcome; falling edge without a fired timer is a tap.
    setSelectHeld(held) {
      if (!onHold) { if (held) this.select(); return; }   // no hold behaviour registered — plain tap-on-press
      if (held) {
        holdFired = false;
        holdTimer = setTimeout(() => {
          holdFired = true;
          if (on && pos) onHold(pos.x, pos.y);
        }, HOLD_MS);
      } else {
        if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
        if (!holdFired && on && pos && onSelect) onSelect(pos.x, pos.y);
      }
    },
    // Re-clamp + repaint after the clamp rect changed (a resize) without moving the cursor itself.
    resize() { clamp(); paint(); },
    // Force the crosshair invisible (true) or let it paint normally again (false) — docs/tgt-keybind-nav.md.
    // `pos`/`vec` are untouched, so un-hiding shows it exactly where it was, and a still-deflected
    // vector keeps driving underneath while hidden (paint() just skips the visible repaint).
    setHidden(h) { hidden = !!h; paint(); },
    // Drop the cursor entirely (a mission boundary) — no lingering position across it.
    reset() {
      if (timer) { cancelAnimationFrame(timer); timer = null; }
      if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
      on = false; pos = null; vec = { x: 0, y: 0 };
      paint();
    },
  };
}
