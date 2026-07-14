// TelemetrySource — the MAP page's "data provider" half, split out from the map view so each has
// a single responsibility (SRP). It owns the ONE EventSource('/stream') connection, parses each
// frame, and is the source of truth for telemetry in the whole MFD:
//   • derives the per-page slices (status/loadout/cm/tgp/targets/rwr/mw/avn/follow) and posts them
//     UP to the shell, which re-forwards them to the other pages; and
//   • hands the raw parsed frame to the local map view via callbacks so it can render.
// It knows nothing about canvas, DOM, zoom/pan, or gestures — that all lives in map.js (the view),
// which instantiates this and consumes it. Co-located in the same iframe on purpose: the view
// needs the full frame every tick, so keeping the parse in-process avoids serializing it across an
// iframe boundary (see src/web/README.md — MAP is the telemetry tap, deliberately).
//
// View → source:  new TelemetrySource({ onFrame, onNoMission, onStatus }).connect()
//                 .emitFollow(on)        — the view's FLW toggle, mirrored up
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

export class TelemetrySource {
  constructor({ onFrame, onNoMission, onStatus } = {}) {
    this._onFrame = onFrame;
    this._onNoMission = onNoMission;
    this._onStatus = onStatus;
    this._lastMsgAt = 0;
    this._inMission = false;            // true between the first frame and the next no-mission ping
    this._meta = null;                  // { w, h, ox, oy } — for target grid labels; persists until reset
    this._lastStatus = { cls: 'disconnected', text: '● DISCONNECTED' };
  }

  connect() {
    const es = new EventSource('/stream');
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

  _postUp(msg) {
    if (window.parent !== window) window.parent.postMessage(Object.assign({ mfd: true }, msg), '*');
  }

  _onMessage(e) {
    this._lastMsgAt = performance.now();
    const d = JSON.parse(e.data);

    if (d.ping) {
      this._setStatus('waiting', '● CONNECTED — no mission');
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

    // Countermeasures (-1 = the aircraft has no such system).
    this._postUp({
      type: 'cm',
      flares:    typeof d.flares    === 'number' ? d.flares    : -1,
      flaresMax: typeof d.flaresMax === 'number' ? d.flaresMax : -1,
      ewKJ:      typeof d.ewKJ      === 'number' ? d.ewKJ      : -1,
      ewKJMax:   typeof d.ewKJMax   === 'number' ? d.ewKJMax   : -1,
      cmCat:     d.cmCat || 0,
    });

    // TGP feed state (so the MFD's TGP page can swap to NO TARGET when the feed stops).
    this._postUp({ type: 'tgp', active: !!d.tgpActive });

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
        targets.push({ id: u.id, n: u.t, g: gridLabel(u.x, u.z, this._meta), r: Math.hypot(dx, dz) / 1000, f: u.f });
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

    // Aircraft name + per-part HP (the AVN damage silhouette; assets fetched on demand by the page).
    this._postUp({
      type: 'avn',
      name: d.name || null,
      parts: Array.isArray(d.parts) ? d.parts : null,
      failures: Array.isArray(d.failures) ? d.failures : null,
      fuel:     typeof d.fuel === 'number' ? d.fuel : -1,
      throttle: typeof d.thr  === 'number' ? d.thr  : -1,
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

    // Loadout (the WPN page mirrors it without opening its own /stream).
    this._postUp({ type: 'loadout', items: d.loadout || [], selWeapon: d.selWeapon || null });

    // TGT filter panel — pass the mod's "tgt" block straight through (present:false when the game's
    // TargetListSelector isn't up). The TGT page renders the toggle states and drives the tgt.* cmds.
    this._postUp(Object.assign({ type: 'tgt' }, d.tgt || { present: false }));
  }

  // On mission exit, tell every consumer the data is gone so no page renders stale state.
  _emitEmpties() {
    this._postUp({ type: 'loadout', items: [], selWeapon: null });
    this._postUp({ type: 'cm', flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 });
    this._postUp({ type: 'tgp', active: false });
    this._postUp({ type: 'targets', items: [] });
    this._postUp({ type: 'rwr', items: [] });
    this._postUp({ type: 'mw', items: [] });
    this._postUp({ type: 'avn', name: null, parts: null, failures: null, fuel: -1, throttle: -1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false });
    this._postUp({ type: 'tgt', present: false });
    this._postUp({ type: 'follow', on: false });
  }
}
