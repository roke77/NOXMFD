<script>
/* ───────────────────────────────────────────────────────────────────────────
   PREVIEW MOCK — injected into the page by tools/build_preview.py.
   NOT part of the shipped HUD. It stands in for the game + mod so the frontend
   can be previewed in any browser (file://) with no Nuclear Option running:
     • window.EventSource  → a synthetic /stream serving one STATIC frame
     • <img> src for /map, /icon, /weapon → real captured assets if available,
       otherwise inline synthetic art (data URIs)

   Two data sources, in priority order:
     1. A real capture (tools/capture_assets.py while in-game) injected as
        window.__PREVIEW_FRAME__ + window.__PREVIEW_ASSETS__ — real map, icons,
        weapon names/icons, contacts and loadout.
     2. The built-in synthetic fallback below (no game ever required).

   The telemetry is intentionally static — game-served values don't change. Only
   the genuinely client-side features (zoom, pan, follow) stay interactive.
   ─────────────────────────────────────────────────────────────────────────── */
(function () {
  const CAPTURE = window.__PREVIEW_ASSETS__ || null;   // real game assets, if captured
  const svg = s => 'data:image/svg+xml;base64,' + btoa(s);   // SVGs must stay ASCII for btoa

  // ── Synthetic fallback art (only used when there's no capture) ────────────────
  const MAP_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
    <rect width="1024" height="1024" fill="#0b1a2a"/>
    <path d="M120,700 Q280,520 420,560 T720,500 Q860,560 900,720 L900,904 L120,904 Z" fill="#1e3a26"/>
    <path d="M140,180 Q320,120 460,200 Q540,250 500,360 Q380,420 240,360 Q120,300 140,180 Z" fill="#23402b"/>
    <circle cx="648" cy="300" r="64" fill="#16304a"/>
    <g stroke="rgba(140,180,140,0.14)" stroke-width="1">
      ${Array.from({ length: 15 }, (_, i) => {
        const p = (i + 1) * 64;
        return `<line x1="${p}" y1="0" x2="${p}" y2="1024"/><line x1="0" y1="${p}" x2="1024" y2="${p}"/>`;
      }).join('')}
    </g>
    <g stroke="#3a5a3a" stroke-width="3"><line x1="430" y1="760" x2="512" y2="700"/></g>
    <text x="20" y="40" fill="rgba(120,160,120,0.5)" font-family="monospace" font-size="22">N</text>
  </svg>`;

  const ICON_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><polygon points="12,2 20,21 12,16 4,21" fill="#ffffff"/></svg>`;

  const WEAPON_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="40" height="96" viewBox="0 0 40 96"><rect x="16" y="12" width="8" height="62" rx="4" fill="#5a7a5a"/><polygon points="20,2 26,16 14,16" fill="#7aa07a"/><polygon points="14,70 8,90 16,82" fill="#5a7a5a"/><polygon points="26,70 32,90 24,82" fill="#5a7a5a"/></svg>`;

  // 4x4 dot grid for IR flares — mostly lit, one dim
  const CM_FLARES_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">${
    Array.from({length:16},(_,i)=>{const x=(i%4)*16+8,y=Math.floor(i/4)*16+8;const dim=i===0;return `<circle cx="${x}" cy="${y}" r="4" fill="${dim?'#1a4a1a':'#39ff14'}"/>`;}).join('')
  }</svg>`;
  // Stylised radar wave for the radar jammer
  const CM_JAMMER_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="48" viewBox="0 0 64 48"><path d="M8,40 L16,8 L24,40 L32,8 L40,40 L48,8 L56,40" stroke="#39ff14" stroke-width="3" fill="none" stroke-linejoin="round"/></svg>`;

  const MOCK_MAP = svg(MAP_SVG), MOCK_ICON = svg(ICON_SVG), MOCK_WEAPON = svg(WEAPON_SVG);
  const MOCK_CM = { flares: svg(CM_FLARES_SVG), jammer: svg(CM_JAMMER_SVG) };
  // Zero-byte GIF data URI: the browser parses it, fails to decode, and fires the
  // <img>'s `error` event WITHOUT a network request. Used to mimic a 404 response
  // for assets a real capture doesn't include — same client-side effect (onerror →
  // retry → square fallback) but no console-visible HTTP 404.
  const SILENT_FAIL_IMG = 'data:image/gif;base64,';
  // 1×1 transparent PNG — mirrors the mod's "no icon" sentinel (TelemetryServer.NoIconPng).
  // For icon-less types the game serves this with HTTP 200; the HUD detects the 1×1 size,
  // stops re-requesting, and draws its square fallback. Used here so the preview exercises
  // that same onload path instead of the onerror/retry one.
  const NO_ICON_IMG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDJ/pLvAAAAAElFTkSuQmCC';

  // ── Reroute the page's image fetches ─────────────────────────────────────────
  const qp = (v, k) => new URLSearchParams(v.split('?')[1] || '').get(k);
  function rewrite(v) {
    if (typeof v !== 'string') return v;
    if (CAPTURE) {
      // Real capture: serve the saved asset, or fall through to a silent-error data URI
      // so the page's onerror path still fires (→ square fallback for missing icons),
      // but the browser console doesn't get spammed with 404s against the preview server.
      // The game-side mod retries until the asset exists; in offline preview it never
      // will, but that's fine — the page handles the missing state gracefully.
      if (v.indexOf('/map') === 0)             return CAPTURE['map'] || v;
      if (v.indexOf('/icon') === 0)            return CAPTURE['icon:' + qp(v, 'type')] || NO_ICON_IMG;
      if (v.indexOf('/weapon') === 0)          return CAPTURE['weapon:' + qp(v, 'name')] || SILENT_FAIL_IMG;
      // TGP MJPEG stream — there's no static asset to capture for a live video feed,
      // so silently fail the request. The TGP page's error handler clears .has-feed
      // → the panel shows its "NO LOCK" empty state, matching the game's no-target case.
      if (v.indexOf('/tgp.mjpg') === 0)        return SILENT_FAIL_IMG;
      if (v.indexOf('/cm') === 0)              return CAPTURE['cm:' + qp(v, 'type')] || MOCK_CM[qp(v, 'type')] || v;
      // Same silent-fail trick as /icon and /weapon — captures rarely cover every
      // (aircraft × part) combo, and the AVN page tolerates missing parts (a failed
      // mask just means that part's silhouette doesn't render). Returning the 0-byte
      // data URI keeps the console clean instead of spamming 404s for each part.
      if (v.indexOf('/airframe?') === 0)       return CAPTURE['airframe:' + qp(v, 'type') + '|' + qp(v, 'part')] || SILENT_FAIL_IMG;
      return v;
    }
    // Synthetic fallback.
    if (v.indexOf('/map') === 0)    return MOCK_MAP;
    if (v.indexOf('/icon') === 0)   return MOCK_ICON;
    if (v.indexOf('/weapon') === 0) return MOCK_WEAPON;
    if (v.indexOf('/cm') === 0)     return MOCK_CM[qp(v, 'type')] || v;
    return v;
  }
  const desc = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src');
  Object.defineProperty(HTMLImageElement.prototype, 'src', {
    configurable: true, enumerable: true,
    get() { return desc.get.call(this); },
    set(v) { desc.set.call(this, rewrite(v)); },
  });

  // ── Reroute the page's fetch() calls ────────────────────────────────────────
  // The AVN page fetches /airframe-layout via fetch(). With a real capture we serve
  // the inlined JSON from the manifest; without one, we synthesise a tiny layout so
  // the page still has something to render (no silhouette images, just empty rects).
  const origFetch = window.fetch.bind(window);
  window.fetch = function(input, init) {
    const u = typeof input === 'string' ? input : (input && input.url) || '';
    if (u.indexOf('/airframe-layout') === 0) {
      const type = qp(u, 'type');
      const j = (CAPTURE && CAPTURE['airframe-layout:' + type]) || null;
      if (j) return Promise.resolve(new Response(JSON.stringify(j), { status: 200, headers: { 'content-type': 'application/json' } }));
      return Promise.resolve(new Response('', { status: 404 }));
    }
    return origFetch(input, init);
  };

  // ── Rewrite CSS mask URLs on the AVN page ──────────────────────────────────
  // The MFD's AVN renderer sets el.style.maskImage = 'url("/airframe?...")' on a
  // *detached* element, then appendChild()s it. CSS resources aren't fetched until
  // the element is in the document, so intercepting attach is the right hook: we
  // rewrite mask URLs synchronously inside appendChild/insertBefore, before the
  // browser ever sees the original /airframe URL.
  // (CSSStyleDeclaration.prototype doesn't expose a maskImage setter we can wrap
  // in Chromium, and a MutationObserver runs too late — the 404 already fired.)
  function rewriteMaskUrlsOn(el) {
    if (!el || el.nodeType !== 1) return;
    const parts = el.classList && el.classList.contains('avn-part')
      ? [el]
      : (el.querySelectorAll ? el.querySelectorAll('.avn-part') : []);
    for (const p of parts) {
      for (const prop of ['maskImage', 'webkitMaskImage']) {
        const v = p.style[prop];
        if (!v || v.indexOf('/airframe') === -1) continue;
        const rewritten = v.replace(/url\(["']?([^)"']+)["']?\)/g, function(m, u) {
          return 'url("' + rewrite(u) + '")';
        });
        if (rewritten !== v) p.style[prop] = rewritten;
      }
    }
  }
  const origAppendChild  = Node.prototype.appendChild;
  const origInsertBefore = Node.prototype.insertBefore;
  Node.prototype.appendChild = function(node) {
    rewriteMaskUrlsOn(node);
    return origAppendChild.call(this, node);
  };
  Node.prototype.insertBefore = function(node, ref) {
    rewriteMaskUrlsOn(node);
    return origInsertBefore.call(this, node, ref);
  };

  // ── Static telemetry frame ───────────────────────────────────────────────────
  // Prefer a real captured frame; otherwise this hand-written synthetic one.
  const DEFAULT_FRAME = {
    name: 'FS-12 Revoker',
    colors: { n: '#9aa0a6', f: '#39ff14', e: '#ff4040' },
    map: { valid: true, w: 100000, h: 100000, ox: 50000, oy: 50000 },
    mapName: 'PREVIEW ISLAND', mission: 'Frontline Patrol — MOCK DATA',
    world: { x: -3000, y: 2500, z: 2000 },
    tas: 248, agl: 2500, hdg: 45,
    gear: 'up', flares: 60, flaresMax: 64, ewKJ: 820, ewKJMax: 1000, cmCat: 1,
    fuel: 0.94, thr: 0.60,
    iconOrient: true, iconScale: 1.1, selWeapon: 'AIM-9X',
    loadout: [
      { n: 'AIM-9X',   a: 2, f: 2 },
      { n: 'AIM-120D', a: 4, f: 6 },
      { n: 'GBU-12',   a: 0, f: 2 },
    ],
    contacts: [
      { t: 'Airbase', f: 1, x: -8000,  z: 12000,  h: 0,   o: false, s: 1 },
      { t: 'F18',     f: 1, x: 3000,   z: 4000,   h: 60,  o: true,  s: 1 },
      { t: 'Su57',    f: 2, x: 16000,  z: -9000,  h: 220, o: true,  s: 1 },
      { t: 'Su57',    f: 2, x: 19000,  z: -6000,  h: 205, o: true,  s: 1 },
      { t: 'SAM',     f: 2, x: -2000,  z: -15000, h: 0,   o: false, s: 1 },
      { t: 'Vessel',  f: 0, x: -14000, z: -4000,  h: 0,   o: false, s: 1 },
    ],
    // 12 mock target locks — the MFD's TGL page displays the first 10, the last 2 stay
    // queued in memory until one of the visible ones is deselected. `f` matches the contact
    // faction code (0 = neutral, 1 = friendly, 2 = enemy) and drives the row colour.
    targets: [
      { n: 'HLT Flatbed',   g: 'Kg53', r: 8.4,  f: 2 },
      { n: 'BMP-2',         g: 'Kh54', r: 9.1,  f: 2 },
      { n: 'F-18',          g: 'Kh55', r: 9.6,  f: 1 },
      { n: 'ZSU-23-4',      g: 'Lh55', r: 10.3, f: 2 },
      { n: 'Vessel',        g: 'Lh56', r: 11.0, f: 0 },
      { n: 'SA-15 Tor',     g: 'Lj57', r: 12.4, f: 2 },
      { n: 'Airbase',       g: 'Lj58', r: 12.9, f: 1 },
      { n: 'Truck',         g: 'Mj58', r: 13.5, f: 0 },
      { n: 'Su-25 (gnd)',   g: 'Mj59', r: 14.2, f: 2 },
      { n: 'Pantsir-S1',    g: 'Mk59', r: 15.0, f: 2 },
      { n: 'KamAZ Fuel',    g: 'Mk60', r: 16.1, f: 2 },
      { n: 'Radar Mast',    g: 'Nk60', r: 17.3, f: 2 },
    ],
    // Radar-warning emitters (rwr) aren't listed here as fixed world positions — they're
    // synthesised below against the ACTIVE frame's ownship, so they sit at the intended
    // bearings whether this synthetic frame or a real capture (different ownship) is in use.
  };
  // Prefer the captured frame when present, but layer DEFAULT_FRAME on top for any field the
  // capture doesn't carry (e.g. older captures predating the `targets` list).
  const FRAME = window.__PREVIEW_FRAME__
    ? Object.assign({}, DEFAULT_FRAME, window.__PREVIEW_FRAME__,
        { targets: window.__PREVIEW_FRAME__.targets || DEFAULT_FRAME.targets })
    : DEFAULT_FRAME;

  // Preview-only: pad the loadout to 6 weapons so the WPN page paginates in both layouts —
  // full view (5 per page → 5 + 1) and the split pane (WPN_SPLIT_MAX=4 → 4 + 2). These
  // synthetic rows have no captured weapon icon, so the single-pane selected-weapon image
  // silently falls back; split mode shows no image anyway. One row is left depleted (a:0)
  // to exercise the empty/red treatment.
  const PREVIEW_EXTRA_WEAPONS = [
    { n: 'AGR-24 Kingpin', a: 6, f: 6 },
    { n: 'AGM-68',         a: 0, f: 2 },
    { n: 'GBU-12 Paveway', a: 4, f: 4 },
  ];
  if (Array.isArray(FRAME.loadout)) {
    for (const w of PREVIEW_EXTRA_WEAPONS) {
      if (FRAME.loadout.length >= 6) break;
      if (!FRAME.loadout.some(x => x.n === w.n)) FRAME.loadout.push(w);
    }
  }

  // Synthetic radar emitters for the RWR page, authored as design intent (bearing relative to
  // the nose + signal power) and converted to world x,z against the active frame's ownship —
  // so they land at the intended bearings/ranges whether the frame is the synthetic one above
  // or a real capture (which carries a different world/hdg). This also round-trips the real
  // wire shape: we emit x,z + tr/pw, and ClientPage converts it right back to az + radius.
  // A real capture that already includes its own `rwr` is left untouched.
  const SYNTH_RWR = [
    { az: 28,  pw: 0.66, tr: 2, n: 'SA-10',  k: 1, period: 2.4 },   // lock,   close, ground-SAM
    { az: 104, pw: 0.40, tr: 1, n: 'SA-11',  k: 1, period: 1.6 },   // track,  mid
    { az: 312, pw: 0.14, tr: 0, n: 'EWR',    k: 1, period: 4.0 },   // search, far, slow sweep
    { az: 200, pw: 0.28, tr: 0, n: 'MIG-29', k: 2, period: 3.0 },   // search, mid-far, air
  ];
  const RWR_TTL = [1, 2, 4];   // per-tier ping lifetime (matches the backend decay)
  if (!Array.isArray(FRAME.rwr) || !FRAME.rwr.length) {
    const ow = FRAME.world || { x: 0, z: 0 }, hdg = FRAME.hdg || 0;
    FRAME.rwr = SYNTH_RWR.map(c => {
      const rng = 8000 + (1 - c.pw) * 38000;            // closer (higher power) = shorter range
      const ab = (c.az + hdg) * Math.PI / 180;
      return { x: Math.round(ow.x + Math.sin(ab) * rng), z: Math.round(ow.z + Math.cos(ab) * rng),
               tr: c.tr, pw: c.pw, fr: 1, n: c.n, k: c.k, _p: c.period };
    });
  }
  // Synthetic incoming missile for the RWR launch indicator. Authored as a bearing + an
  // animated range that closes from _r0 to _r1 km over _period s (then loops), so the preview
  // shows the connecting line shortening as it bears in. mwTickApproach below recomputes its
  // world x,z each send. A real frame's own `mw` is left alone.
  if (!Array.isArray(FRAME.mw)) {
    FRAME.mw = [{ x: 0, z: 0, st: 'ARH', _az: 150, _r0: 6.0, _r1: 0.4, _period: 6 }];
  }
  function mwTickApproach() {
    if (!Array.isArray(FRAME.mw)) return;
    const ow = FRAME.world || { x: 0, z: 0 }, hdg = FRAME.hdg || 0, now = performance.now() / 1000;
    for (const e of FRAME.mw) {
      if (typeof e._az !== 'number') continue;
      const t = (now % e._period) / e._period;
      const rng = (e._r0 + t * (e._r1 - e._r0)) * 1000;   // km -> m
      const ab = (e._az + hdg) * Math.PI / 180;
      e.x = Math.round(ow.x + Math.sin(ab) * rng);
      e.z = Math.round(ow.z + Math.cos(ab) * rng);
    }
  }
  // Animate each synthetic emitter's ping freshness (fr): bright on each sweep (every _p
  // seconds), fading to 0 over its tier lifetime — so the preview shows the diamonds "ping"
  // without a live game. A real capture carries its own fr and has no _p marker, so it's left
  // alone. Called each frame send below.
  function rwrTickFreshness() {
    if (!Array.isArray(FRAME.rwr)) return;
    const now = performance.now() / 1000;
    for (const e of FRAME.rwr) {
      if (typeof e._p !== 'number') continue;
      e.fr = Math.max(0, 1 - (now % e._p) / (RWR_TTL[e.tr] || 1));
    }
  }

  // ── Stand-in EventSource: drives the page exactly like the real /stream ───────
  class MockEventSource {
    constructor(url) {
      this.url = url; this.onmessage = null; this.onerror = null; this.onopen = null;
      // The frame is static EXCEPT the RWR ping freshness, which we re-tick each send so the
      // diamonds visibly pulse; ~6.7 Hz approximates the real 10 Hz stream (and keeps the
      // page's 2.5 s connection watchdog happy).
      const tick = () => { rwrTickFreshness(); mwTickApproach(); this._send(JSON.stringify(FRAME)); };
      setTimeout(() => {
        tick();
        this._timer = setInterval(tick, 150);
      }, 30);
    }
    _send(data) { if (this.onmessage) this.onmessage({ data }); }
    close() { clearInterval(this._timer); }
  }
  window.EventSource = MockEventSource;

  // Show the map immediately (avoids the initial /map 404 flash).
  window.addEventListener('DOMContentLoaded', () => {
    const mi = document.getElementById('map-img');
    if (mi) mi.src = '/map';                          // routed by the setter above
  });
})();
</script>
