// TelemetrySource owns the ONE EventSource('/stream') connection, parses each frame, and is the
// source of truth for telemetry in the whole MFD:
//   • derives the slices (status/loadout/cm/tgp/targets/rwr/mw/avn/follow, and mapinfo) and posts
//     them UP to the shell, which re-forwards them to the other pages. All but the last are
//     per-page; mapinfo is for shell chrome that shows no map — see _emit; and
//   • hands the raw parsed frame to the local map view via callbacks so it can render.
// It knows nothing about canvas, DOM, zoom/pan, or gestures — that lives in map.js (the view),
// which instantiates this and consumes it. Co-located with the view in the same iframe: the view
// needs the full frame every tick, so keeping the parse in-process avoids serializing it across an
// iframe boundary (MAP is the telemetry tap — see src/web/README.md).
//
// View → source:  new TelemetrySource({ onFrame, onNoMission, onStatus }).connect()
//                 .emitFollow(on)        — the view's FLW toggle, mirrored up
//                 .emitGrid(on)          — the view's GRID toggle, mirrored up
//                 .rebroadcastStatus()   — answer the shell's status-request
// Source → view (callbacks):
//   onFrame(d)          a real telemetry frame arrived — render it
//   onNoMission(didEnd) a ping (connected, no mission); didEnd=true on the frame→ping transition
//                       so the view resets once; called every ping so the view can show NO SIGNAL
//   onStatus(cls, text) connection status changed — update the status readout

// Reproduces the game's grid label (e.g. "Hc87") from world coords + the map offsets in `meta`
// ({w,h,ox,oy}). Pure — shared by the view's HUD readout and this module's target derivation.
export function gridLabel(wx, wz, meta) {
  if (!meta) return '—';
  const vx = meta.ox + wx;
  const vz = meta.oy - wz;
  const majX = Math.floor(vx / 10000), minX = Math.floor((vx - 10000 * majX) / 1000);
  const majZ = Math.floor(vz / 10000), minZ = Math.floor((vz - 10000 * majZ) / 1000);
  if (majX < 0 || majZ < 0) return '—';
  const vert = String.fromCharCode(65 + majZ) + String.fromCharCode(97 + minZ);
  return vert + `${majX}${minX}`;
}

// AKF advanced kill feed (docs/akf-page.md) default/reset shape — shared by the mission-present
// fallback below and _emitEmpties, so the two can't drift out of sync with each other.
const AKF_EMPTY = { all: [], player: [], kills: { aircraft: 0, ship: 0, vehicle: 0, building: 0 },
                     value: 0, fundsGained: 0, fundsSpent: 0 };

// This document's MFD-instance id, for the server's instance registry (SOI — docs/keybinds-page.md).
// sessionStorage, NOT localStorage: sessionStorage is scoped to the browsing context, so it stays
// the same across a reload (a tablet that refreshes stays the instance it was) but differs in a
// second tab (two displays on one PC are two instances) — localStorage is per-origin and every tab
// would claim the same id. Read from an iframe, it resolves to the TAB's store, which is what's
// wanted since the instance is the whole document, not this tap.
//
// Guarded on every hop: sessionStorage throws in some private-mode browsers, and randomUUID needs a
// secure context — plain http:// over the LAN is exactly how this mod is used. A per-load fallback
// id still identifies the instance while it is connected; it just doesn't survive a reload.
function instanceId() {
  const fresh = () =>
    (crypto.randomUUID ? crypto.randomUUID()
                       : 'x' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10));
  try {
    let id = sessionStorage.getItem('noxmfd-cid');
    if (!id) { id = fresh(); sessionStorage.setItem('noxmfd-cid', id); }
    return id;
  } catch (e) {
    return fresh();
  }
}

export class TelemetrySource {
  constructor({ onFrame, onNoMission, onStatus } = {}) {
    this._onFrame = onFrame;
    this._onNoMission = onNoMission;
    this._onStatus = onStatus;
    this._lastMsgAt = 0;
    this._inMission = false;            // true between the first frame and the next no-mission ping
    this._meta = null;                  // { w, h, ox, oy } — for target grid labels; persists until reset
    this._lastStatus = { cls: 'disconnected', text: '● DISCONNECTED' };
    this._cid = '';                     // this instance's SOI id — settled by the server's hello
    this._soiFocused = null;            // null = never reported; forces the first post either way
    this._soiPane = -1;                 // which of this instance's surfaces is focused (-1 = none)
    this._soiSeq = null;                // null = the first frame's counter is a starting point, not a press
    // MAP cursor (docs/map-cursor.md): same idempotent-counter idea as soiSeq/soiAct, plus the
    // continuous vector tracked every frame — see the justFocused note in _onMessage.
    this._cursorX = 0;
    this._cursorY = 0;
    this._cursorSelSeq = null;
    this._mapActSeq = null;
    this._cursorSelHeld = false;   // Cursor Select's live held state (docs/page-cursor.md)
    this._badFrames = 0;           // malformed frames dropped so far (see _onMessage)
    this._lastBadLogAt = 0;        // rate-limits the drop log — a bad frame usually repeats at 10 Hz
  }

  connect() {
    this._cid = instanceId();
    const es = new EventSource('/stream?cid=' + encodeURIComponent(this._cid));
    // The server answers with the id it actually filed us under. Normally that's the one just
    // sent; when storage is unavailable nothing was sent and the server named us itself. Either
    // way this is the id SOI focus is broadcast by, so it's the one to compare against — and it's
    // not persisted, since a server-named id belongs to this connection only.
    es.addEventListener('hello', (e) => {
      try { this._cid = JSON.parse(e.data).cid || this._cid; } catch (err) { /* keep ours */ }
      // Tells the shell which id it is so it can report its surface count (soi.panes) under it.
      // Fires on every hello, including an SSE reconnect — the server resets pane count to 1 on a
      // fresh connection, so the shell must re-report.
      this._postUp({ type: 'soi-cid', cid: this._cid });
    });
    // The MAP cursor rides its own event at a much higher rate than the telemetry frame
    // (docs/map-cursor.md) — a slewed axis is continuous, so latency here is felt directly.
    es.addEventListener('cursor', (e) => {
      try { this._onCursor(JSON.parse(e.data)); } catch (err) { /* malformed — skip this one */ }
    });
    es.onmessage = (e) => this._onMessage(e);
    es.onerror = () => {};   // EventSource auto-reconnects; the watchdog decides when to flag DISCONNECTED
    // Watchdog — tolerate transient SSE blips, only flag disconnect after a real gap.
    setInterval(() => {
      if (performance.now() - this._lastMsgAt > 2500) this._setStatus('disconnected', '● DISCONNECTED — retrying…');
    }, 700);
  }

  // Mirror the connection status to the shell (so MAIN can show it without its own /stream) and
  // to the local view's readout.
  _setStatus(cls, text) {
    this._lastStatus = { cls, text };
    if (this._onStatus) this._onStatus(cls, text);
    this._postUp({ type: 'status', cls, text });
  }
  rebroadcastStatus() { this._postUp({ type: 'status', cls: this._lastStatus.cls, text: this._lastStatus.text }); }
  emitFollow(on) { this._postUp({ type: 'follow', on: !!on }); }
  // A view-local display preference, not derived from the telemetry frame, so unlike 'follow' it
  // has no _emitEmpties reset: there's nothing wrong with the grid staying on/off across a
  // no-mission gap.
  emitGrid(on) { this._postUp({ type: 'grid', on: !!on }); }

  _postUp(msg) {
    if (window.parent !== window) window.parent.postMessage(Object.assign({ mfd: true }, msg), '*');
  }

  // The MAP cursor's own SSE event (docs/map-cursor.md) — arrives far more often than a telemetry
  // frame, and carries only the cursor. Focus state comes from the last frame (this._soiFocused):
  // focus changes are rare and discrete, so they can keep riding the slow channel.
  //
  // The velocity is tracked REGARDLESS of focus so it is never stale on a regain; the presses use
  // the same idempotent-counter shape as soiSeq — act only when the counter CHANGES, and treat the
  // first one seen as a baseline, since presses made before this display connected are history.
  _onCursor(c) {
    this._lastMsgAt = performance.now();
    const x = typeof c.x === 'number' ? c.x : 0;
    const y = typeof c.y === 'number' ? c.y : 0;
    const changed = x !== this._cursorX || y !== this._cursorY;
    this._cursorX = x; this._cursorY = y;
    const pane = this._soiPane;
    if (this._soiFocused && changed) this._postUp({ type: 'cursor', x, y, pane });

    // Cursor Select's LIVE held state (docs/page-cursor.md) — a page with its own tap/long-press
    // controls needs the true→false transition to tell a tap from a hold, which the edge counter
    // below can't express. Tracked regardless of focus, same reasoning as the vector above.
    const held = !!c.held;
    if (held !== this._cursorSelHeld) {
      this._cursorSelHeld = held;
      if (this._soiFocused) this._postUp({ type: 'cursor-held', held, pane });
    }

    if (typeof c.selSeq === 'number' && c.selSeq !== this._cursorSelSeq) {
      const first = this._cursorSelSeq === null;
      this._cursorSelSeq = c.selSeq;
      if (!first && this._soiFocused) this._postUp({ type: 'cursor-select', pane });
    }
    if (typeof c.actSeq === 'number' && c.actSeq !== this._mapActSeq) {
      const first = this._mapActSeq === null;
      this._mapActSeq = c.actSeq;
      if (!first && this._soiFocused && c.act) this._postUp({ type: 'map-act', act: c.act, pane });
    }
  }

  _onMessage(e) {
    this._lastMsgAt = performance.now();
    // Drop a malformed frame instead of dying on it. The server hand-rolls its JSON
    // (TelemetryJson.cs), so a serializer bug lands here as a parse throw — an uncaught one
    // would take down the whole tick's fan-out, freezing every page while the SSE connection stays
    // open and the watchdog below stays quiet. Skipping costs one frame; the next good one
    // (~100 ms) repaints everything. Logged rather than swallowed, since the console error is what
    // locates such bugs; rate-limited so a persistent one leaves the console readable.
    let d;
    try {
      d = JSON.parse(e.data);
    } catch (err) {
      this._badFrames++;
      const now = performance.now();
      if (this._badFrames === 1 || now - this._lastBadLogAt > 5000) {
        this._lastBadLogAt = now;
        console.error('[noxmfd] malformed telemetry frame dropped (' + this._badFrames +
                      ' total this session):', err);
      }
      return;
    }

    // SOI focus rides in every frame kind, ping included — a display is focusable at the main menu,
    // where a ping is all there is — so this sits ahead of the ping branch's early return rather
    // than in _emit with the per-page slices. Focus is a SURFACE: whether this instance is the
    // target AND which of its panes/portals. Posted only on a change (of either), so the shell isn't
    // rebuilt ten times a second to say the same thing; pane travels so it can ring the right pane.
    const focused = !!d.soiTarget && d.soiTarget === this._cid;
    const pane = focused && typeof d.soiPane === 'number' ? d.soiPane : -1;
    const justFocused = focused && !this._soiFocused;
    if (focused !== this._soiFocused || pane !== this._soiPane) {
      this._soiFocused = focused;
      this._soiPane = pane;
      this._postUp({ type: 'soi', focused, pane });
      // map.js zeroes its own cursor when it loses focus, so a regain has to resend the vector
      // even if unchanged since this display was last focused — otherwise the crosshair sits still
      // under an already-deflected stick until the pilot happens to move it.
      if (justFocused) this._postUp({ type: 'cursor', x: this._cursorX, y: this._cursorY, pane });
    }

    // A SOI key press. The counter makes this safe to broadcast: act only when it CHANGES, so a
    // repeated frame can't double-press and a dropped one costs nothing. The first frame only
    // records where the counter is — presses made before this display connected are history, not
    // input. Unfocused displays see the same fields and ignore them; `pane` rides along so the
    // shell acts on the focused surface.
    if (typeof d.soiSeq === 'number' && d.soiSeq !== this._soiSeq) {
      const first = this._soiSeq === null;
      this._soiSeq = d.soiSeq;
      if (!first && focused && d.soiAct) this._postUp({ type: 'soi-act', act: d.soiAct, pane });
    }

    if (d.ping) {
      // A mission can be running with no local aircraft chosen yet (still on the spawn/loadout
      // screen) — that's a real connection, not "no mission", even though there's no telemetry
      // frame to show yet either. missionRunning tells the two apart; d.ping stays true for both,
      // since neither has a real frame to emit.
      if (d.missionRunning) this._setStatus('connected', '● CONNECTED');
      else this._setStatus('waiting', '● CONNECTED — no mission');
      const didEnd = this._inMission;
      if (didEnd) { this._inMission = false; this._meta = null; this._emitEmpties(); }
      if (this._onNoMission) this._onNoMission(didEnd);
      return;
    }

    this._setStatus('connected', '● CONNECTED');
    this._inMission = true;
    if (d.map && d.map.valid) this._meta = { w: d.map.w, h: d.map.h, ox: d.map.ox, oy: d.map.oy };
    this._emit(d);
    if (this._onFrame) this._onFrame(d);
  }

  // Derive every per-page slice from one frame and post them up. Pure transforms of `d` (+ this
  // module's map meta for target grid labels) — no view/render state is read.
  _emit(d) {
    if (window.parent === window) return;   // standalone /map-view: nobody to mirror to

    // -1 = the aircraft has no such countermeasure system.
    this._postUp({
      type: 'cm',
      flares:    typeof d.flares    === 'number' ? d.flares    : -1,
      flaresMax: typeof d.flaresMax === 'number' ? d.flaresMax : -1,
      ewKJ:      typeof d.ewKJ      === 'number' ? d.ewKJ      : -1,
      ewKJMax:   typeof d.ewKJMax   === 'number' ? d.ewKJMax   : -1,
      cmCat:     d.cmCat || 0,
    });

    // TGP feed state (so the MFD's TGP page can swap to NO TARGET when the feed stops), plus the
    // quality mode and stat-overlay block HQ mode draws client-side (see docs/tgp-high-quality-
    // mode.md — Native already has this baked into the video by the game's own TargetScreenUI).
    this._postUp({ type: 'tgp', active: !!d.tgpActive, quality: d.tgpQuality || 'native', data: d.tgp || null });

    // The mission name, the ownship's grid, and the raw position/heading/map-meta a non-map page
    // needs to compute distance/bearing to a waypoint and its own grid labels — for chrome that
    // shows no map (e.g. the F-35's master strip). The map page still renders its own HUD straight
    // from `d`; this is the same pair, derived once more for anyone outside the iframe.
    this._postUp({
      type: 'mapinfo',
      mission: d.mission || null,
      grid: d.world ? gridLabel(d.world.x, d.world.z, this._meta) : null,
      x:   d.world ? d.world.x : null,
      z:   d.world ? d.world.z : null,
      hdg: typeof d.hdg === 'number' ? d.hdg : null,
      ox:  this._meta ? this._meta.ox : null,
      oy:  this._meta ? this._meta.oy : null,
    });

    // Selected-target list. The mod flags each targeted unit on its contact (same `tg` that draws
    // the map's target box), so derive from contacts; a preview mock may override via d.targets.
    let targets;
    if (Array.isArray(d.targets)) {
      targets = d.targets;
    } else if (Array.isArray(d.contacts) && d.world) {
      targets = [];
      for (const u of d.contacts) {
        if (!u.tg) continue;
        const dx = u.x - d.world.x;
        const dz = u.z - d.world.z;
        targets.push({ id: u.id, n: u.t, g: gridLabel(u.x, u.z, this._meta), r: Math.hypot(dx, dz) / 1000, f: u.f, dl: !!u.dl, st: !!u.st });
      }
    } else {
      targets = [];
    }
    this._postUp({ type: 'targets', items: targets });

    // Radar-warning emitters → nose-up plot (az = bearing relative to heading, dist = 1 - power).
    let rwr = [];
    if (Array.isArray(d.rwr) && d.world) {
      const hdg = d.hdg || 0;
      for (const c of d.rwr) {
        const dx = c.x - d.world.x;
        const dz = c.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az % 360) + 360) % 360;
        const pw = Math.max(0, Math.min(1, typeof c.pw === 'number' ? c.pw : 0));
        const fr = typeof c.fr === 'number' ? Math.max(0, Math.min(1, c.fr)) : 1;
        rwr.push({ az: az, d: Math.max(0.06, Math.min(1, 1 - pw)), tr: c.tr || 0, fr: fr, n: c.n || '', k: c.k || 0 });
      }
    }
    this._postUp({ type: 'rwr', items: rwr });

    // Incoming missiles → nose-up bearing (az) + range (rng); nb = beam-notch heading (radar only).
    let mw = [];
    if (Array.isArray(d.mw) && d.world) {
      const hdg = d.hdg || 0;
      for (const m of d.mw) {
        const dx = m.x - d.world.x;
        const dz = m.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az % 360) + 360) % 360;
        const item = { az: az, rng: Math.hypot(dx, dz) / 1000, st: m.st || '' };
        if (typeof m.nb === 'number' && m.nb >= 0) item.nb = (((m.nb - hdg) % 360) + 360) % 360;
        mw.push(item);
      }
    }
    this._postUp({ type: 'mw', items: mw });

    // RDR → nose-up B-scope contacts (docs/rdr-page.md). az = signed bearing off nose, rng = world
    // distance (same units as range), rhdg = travel heading relative to nose (velocity stub). The
    // present/range/cone scope scale passes straight through; the page shows a placeholder when
    // present is false. Posted every tick (even absent) so the page can drop back to placeholder.
    let rdrItems = [];
    const rb = d.rdr;
    if (rb && rb.present && Array.isArray(rb.items) && d.world) {
      const hdg = d.hdg || 0;
      for (const c of rb.items) {
        const dx = c.x - d.world.x;
        const dz = c.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az + 540) % 360) - 180;                          // -180..180 off nose
        const rhdg = ((((c.hdg || 0) - hdg) % 360) + 360) % 360;  // travel heading relative to nose
        rdrItems.push({ id: c.id, az: az, rng: Math.hypot(dx, dz), alt: c.alt || 0, rhdg: rhdg,
                        tg: c.tg || 0, radar: !!c.rd, dl: !!c.dl, n: c.n || '' });
      }
    }
    // Pitbull missiles: the player's own AA missiles whose active-radar seeker has locked. Same
    // az/rng B-scope projection as the ordinary contacts above — independent of rb.present (it's
    // the missile's own radar, not the aircraft's), so this runs off d.rdr.pb directly rather than
    // being gated by rb.present.
    let pbItems = [];
    if (rb && Array.isArray(rb.pb) && d.world) {
      const hdg = d.hdg || 0;
      for (const m of rb.pb) {
        const dx = m.x - d.world.x;
        const dz = m.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az + 540) % 360) - 180;
        const rhdg = ((((m.hdg || 0) - hdg) % 360) + 360) % 360;
        pbItems.push({ id: m.id, az: az, rng: Math.hypot(dx, dz), alt: m.alt || 0, rhdg: rhdg,
                       tid: m.tid || 0 });
      }
    }
    this._postUp({
      type: 'rdr',
      present: !!(rb && rb.present),
      range: rb ? (rb.range || 0) : 0,
      cone: rb ? (rb.cone || 0) : 0,
      metric: !!(rb && rb.metric),
      // Radar emission (Aircraft.HasRadarEmission, already forwarded top-level for AVN's status
      // tile) drives the B-scope's antenna-sweep caret — on only while actively emitting.
      radarOn: d.radar === true,
      // Time.timeSinceLevelLoad, the clock the game's own MFD radar sweep runs on — lets the page
      // phase-lock its caret to the native sweep (docs/rdr-page.md).
      levelTime: rb ? (rb.lvlt || 0) : 0,
      hdg: d.hdg || 0,
      items: rdrItems,
      pb: pbItems
    });

    // Aircraft name + per-part HP (the AVN damage silhouette; assets fetched on demand by the page).
    this._postUp({
      type: 'avn',
      name: d.name || null,
      parts: Array.isArray(d.parts) ? d.parts : null,
      failures: Array.isArray(d.failures) ? d.failures : null,
      // AFM frontal-silhouette hardpoint markers — [{n, c}], c a live hex color mirroring the
      // cockpit's own WEAPON ARMED panel (green = armed/has ammo, red = exhausted).
      pylons: Array.isArray(d.pylons) ? d.pylons : null,
      fuel:     typeof d.fuel === 'number' ? d.fuel : -1,
      throttle: typeof d.thr  === 'number' ? d.thr  : -1,
      heat:     typeof d.heat === 'number' ? d.heat : -1,
      heatColor: typeof d.heatColor === 'string' ? d.heatColor : null,
      rpm:      typeof d.rpm  === 'number' ? d.rpm  : -1,
      // Afterburner gauge shape (static per airframe). hasAb splits the THRL bar at abStart.
      hasAb:    d.hasAb === true,
      abStart:  typeof d.abStart === 'number' ? d.abStart : 1,
      // Avionics status tiles. gear arrives as 'up'|'down'; the rest are bools.
      gearDown: d.gear === 'down',
      radar:    d.radar === true,
      guns:     d.guns  === true,
      ignition: d.ign    === true,
      assist:   d.assist === true,
      turret:   d.turret === true,
      nvg:      d.nvg    === true,
      navLights: d.navlt === true,
    });

    // Loadout (the WPN page mirrors it without opening its own /stream). masterArmsOn is mod state
    // (docs/radar-master-arms.md), not per-loadout, but rides the same message since the WPN page
    // is the only consumer of either — its ARM/SAFE controls always show regardless of the
    // immersion setting, so this is never null/absent, just true or false.
    this._postUp({ type: 'loadout', items: d.loadout || [], selWeapon: d.selWeapon || null,
                   softGun: d.softGun || null, softRel: d.softRel || null,
                   masterArmsOn: d.masterArmsOn === true, combatMode: d.combatMode || 'all' });

    // TGT filter panel — pass the mod's "tgt" block straight through (present:false when the game's
    // TargetListSelector isn't up). The TGT page renders the toggle states and drives the tgt.* cmds.
    this._postUp(Object.assign({ type: 'tgt' }, d.tgt || { present: false }));

    // BDF faction-forces panel (docs/bdf-page.md) — always BOSCALI, a fixed identity. Pass the
    // mod's "bdf" block straight through (present:false when Boscali has no FactionHQ yet).
    // Read-only, no commands.
    this._postUp(Object.assign({ type: 'bdf' }, d.bdf || { present: false }));

    // PAL — the same panel, always PRIMEVA (docs/bdf-page.md). present:false when Primeva has no
    // FactionHQ yet.
    this._postUp(Object.assign({ type: 'pal' }, d.pal || { present: false }));

    // MIS mission-info panel (docs/md-pages.md) — name, time/duration, escalation score/level, and
    // the mission's own description text. present:false in multiplayer or between missions.
    this._postUp(Object.assign({ type: 'mis' }, d.mis || { present: false }));

    // OBJ active-objectives list (docs/md-pages.md). present:false when the player faction's HQ
    // isn't resolved yet. Each objective's position sub-rows arrive as raw world x/z from the
    // plugin; grid label and live range are derived here the same way targets/rwr/mw already are,
    // so range stays live at the base frame's own rate rather than the plugin's 1 Hz refresh.
    const objBlock = d.obj || { present: false };
    const objItems = [];
    if (objBlock.present && Array.isArray(objBlock.items)) {
      for (const o of objBlock.items) {
        const positions = [];
        if (Array.isArray(o.pos) && d.world) {
          for (const p of o.pos) {
            const dx = p.x - d.world.x, dz = p.z - d.world.z;
            positions.push({ n: p.n, g: gridLabel(p.x, p.z, this._meta), r: Math.hypot(dx, dz) / 1000 });
          }
        }
        objItems.push({ n: o.n, s: o.s, p: o.p, pos: positions });
      }
    }
    this._postUp({ type: 'obj', present: !!objBlock.present, items: objItems });

    // AKF advanced kill feed (docs/akf-page.md). Always present while a mission runs (no
    // present:false gate like MIS/OBJ) — an empty session just reads as all-zero. all is
    // everyone's kills; player/kills/value/fundsGained/fundsSpent are scoped to the local player's
    // own kills only.
    this._postUp(Object.assign({ type: 'akf' }, d.akf || AKF_EMPTY));

    // Extension telemetry (docs/extensions-api.md) — one generic forward for every registered
    // extension's slice, rather than a named block per extension like everything above. Wrapped in
    // `data` rather than spread, since — unlike NOXMFD's own blocks — its shape isn't known here
    // and might not even be a plain object (an extension could publish an array or a bare number).
    if (d.ext) {
      for (const id of Object.keys(d.ext)) this._postUp({ type: 'ext_' + id, data: d.ext[id] });
    }
  }

  // On mission exit, tell every consumer the data is gone so no page renders stale state.
  _emitEmpties() {
    this._postUp({ type: 'loadout', items: [], selWeapon: null, softGun: null, softRel: null, masterArmsOn: true, combatMode: 'all' });
    this._postUp({ type: 'cm', flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 });
    this._postUp({ type: 'tgp', active: false, quality: 'native', data: null });
    this._postUp({ type: 'mapinfo', mission: null, grid: null, x: null, z: null, hdg: null, ox: null, oy: null });
    this._postUp({ type: 'targets', items: [] });
    this._postUp({ type: 'rwr', items: [] });
    this._postUp({ type: 'mw', items: [] });
    this._postUp({ type: 'avn', name: null, parts: null, failures: null, pylons: null, fuel: -1, throttle: -1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false });
    this._postUp({ type: 'tgt', present: false });
    this._postUp({ type: 'bdf', present: false });
    this._postUp({ type: 'pal', present: false });
    this._postUp({ type: 'mis', present: false });
    this._postUp({ type: 'obj', present: false });
    this._postUp(Object.assign({ type: 'akf' }, AKF_EMPTY));
    this._postUp({ type: 'follow', on: false });
  }
}
