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

  // ── Reroute the page's image fetches ─────────────────────────────────────────
  const qp = (v, k) => new URLSearchParams(v.split('?')[1] || '').get(k);
  function rewrite(v) {
    if (typeof v !== 'string') return v;
    if (CAPTURE) {
      // Real capture: serve the saved asset, or fall through to the original URL so a
      // missing icon 404s and the page shows its square fallback — exactly like the game.
      if (v.indexOf('/map') === 0)    return CAPTURE['map'] || v;
      if (v.indexOf('/icon') === 0)   return CAPTURE['icon:' + qp(v, 'type')] || v;
      if (v.indexOf('/weapon') === 0) return CAPTURE['weapon:' + qp(v, 'name')] || v;
      if (v.indexOf('/cm') === 0)     return CAPTURE['cm:' + qp(v, 'type')] || MOCK_CM[qp(v, 'type')] || v;
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
  };
  const FRAME = window.__PREVIEW_FRAME__ || DEFAULT_FRAME;

  // ── Stand-in EventSource: drives the page exactly like the real /stream ───────
  class MockEventSource {
    constructor(url) {
      this.url = url; this.onmessage = null; this.onerror = null; this.onopen = null;
      const json = JSON.stringify(FRAME);
      // Send the static frame once, then resend every 1 s purely to keep the page's
      // connection watchdog happy (2.5 s timeout). The data never changes.
      setTimeout(() => {
        this._send(json);
        this._timer = setInterval(() => this._send(json), 1000);
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
