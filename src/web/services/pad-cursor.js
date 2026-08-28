// The PAD cursor: a crosshair standing in for the mouse/touch while a page is the HOTAS-driven
// SOI's focused surface (docs/page-cursor.md, docs/map-cursor.md). Integrates a velocity vector
// locally at ~60 Hz between the ~10 Hz transport frames, clamped to a page-supplied rect, plus a
// discrete Select edge. Owns the crosshair element, its position, and the integrator; knows
// nothing about what's under the cursor — that's the caller's onSelect/onHold/onMove.
//
// SPEED is a flat constant tuned by feel (crosses a typical panel in ~1.5-2s at full deflection);
// a config entry can come later if anyone wants to tune it live instead.
const DEFAULT_SPEED = 700;   // px/second at full deflection
const DEFAULT_HOLD_MS = 500; // matches the DOM press/hold pages already use

// clampRect() returns { dx, dy, dw, dh } — the rect (in the same px space as the crosshair
// element's positioned ancestor) the cursor may not leave.
//
// onSelect(x, y)     — Select released before holdMs, or no onHold registered at all: the caller
//                       decides what "select" means (a hit-test, DOM elementFromPoint, etc).
// onHold(x, y)       — optional: Select held past holdMs, a page's long-press equivalent
//                       (docs/page-cursor.md). Omitting it makes every press a plain onSelect.
// onMove(x, y)       — optional: called with the cursor's current point whenever it's visible and
//                       placed, and with (null, null) when hidden/unfocused, so a page can
//                       highlight whatever's under the crosshair (docs/page-cursor.md #2).
// onEdge(ex, ey, dt) — optional: called during integration when the pre-clamp position has
//                       overshot clampRect by (ex, ey) px, so a page can react (e.g. pan its view
//                       under a screen-pinned cursor, docs/page-cursor.md #3). The cursor is
//                       clamped to the rect regardless, right after this fires.
export function createPadCursor({ el, clampRect, onSelect, onHold, onMove, onEdge, speed, holdMs }) {
  const SPEED = speed || DEFAULT_SPEED;
  const HOLD_MS = holdMs || DEFAULT_HOLD_MS;
  let on = false;            // is this surface the SOI's focused MAP/TGT/HUD right now?
  let pos = null;             // {x,y} in clampRect's px space; null when not focused or not yet placed
  let vec = { x: 0, y: 0 };   // last reported [-1,1] velocity, held between broadcasts
  let timer = null, lastT = 0;
  let holdTimer = null, holdFired = false;   // Select press/hold arbitration (see setSelectHeld)
  // Externally forced invisible (docs/tgt-keybind-nav.md): a page can have its own selection mode
  // (e.g. a row-stepper) that's mutually exclusive with the free crosshair. setHidden suppresses
  // the crosshair without touching `on`/`pos`, so un-hiding resumes exactly where it was.
  let hidden = false;

  function clamp() {
    if (!pos) return;
    const r = clampRect();
    pos.x = Math.max(r.dx, Math.min(r.dx + r.dw, pos.x));
    pos.y = Math.max(r.dy, Math.min(r.dy + r.dh, pos.y));
  }

  // Transform, not left/top, so moving the cursor stays a compositor-only operation and never
  // reflows/repaints the page underneath it.
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

  // Integrates the last-known velocity between broadcasts using performance.now() rather than the
  // rAF timestamp, so it doesn't depend on how the embedder invokes requestAnimationFrame.
  // Self-stops once the vector is zero; ensureAnim restarts it.
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
    // Center only the first time this surface is focused; afterward leave the cursor wherever the
    // pilot parked it — losing focus hides it rather than forgetting its position.
    setFocus(v, centerX, centerY) {
      on = v;
      if (on && !pos) pos = { x: centerX, y: centerY };
      if (!on) vec = { x: 0, y: 0 };
      paint();
    },
    setVector(x, y) { vec = { x: x || 0, y: y || 0 }; ensureAnim(); },
    // Default select path for a press that never needs to be told apart from a hold, or for any
    // page that doesn't pass onHold.
    select() { if (on && pos && onSelect) onSelect(pos.x, pos.y); },
    // Meaningful only when the page passed onHold. Mirrors a pointerdown/pointerup pair: rising
    // edge arms a HOLD_MS timer (fires while still held → hold outcome); falling edge without a
    // fired timer is a tap.
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
    // Current cursor position (clampRect's px space), or null when not shown — focused, placed,
    // and not hidden. A page can use this as a zoom/action anchor when the cursor is standing in
    // for the mouse (issue #64: bezel/keybind zoom should target the cursor, same as a mouse wheel
    // targets the pointer).
    getPos() { return (on && pos && !hidden) ? { x: pos.x, y: pos.y } : null; },
    // `pos`/`vec` stay untouched while hidden, so a still-deflected vector keeps driving
    // underneath (paint() just skips the visible repaint), and un-hiding shows the cursor exactly
    // where it was.
    setHidden(h) { hidden = !!h; paint(); },
    // Drop the cursor entirely (e.g. a mission boundary) — no lingering position across it.
    reset() {
      if (timer) { cancelAnimationFrame(timer); timer = null; }
      if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; }
      on = false; pos = null; vec = { x: 0, y: 0 };
      paint();
    },
  };
}
