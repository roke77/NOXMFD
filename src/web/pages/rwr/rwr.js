// RWR page — radar-warning scope. A pure reactive renderer driven by the shell over
// postMessage; single source of truth for BOTH layouts (full-screen iframe + split pane).
// The full-view overlay twin in MfdPage.cs is gone. See rwr.html for the message contract.

// Contacts: each 'rwr' postMessage carries nose-up plot data, already converted by ClientPage —
// { az (deg clockwise from nose), d (0..1 radius), tr (tier 0 search / 1 track / 2 lock),
// fr (0..1 ping freshness), n (label), k (kind) }.
var RWR_COL = ['#dcdcdc', '#ffd21e', '#ff3b30'];
var rwrItems = [];
function rwrShort(n) {
  if (!n) return '';
  var s = String(n).split(/\s+/)[0].toUpperCase();
  return s.length > 7 ? s.slice(0, 7) : s;
}
function renderRwr() {
  var g = document.getElementById('rwr-contacts');
  if (!g) return;
  var cx = 500, cy = 500, R = 460, out = '';
  (rwrItems || []).forEach(function(c) {
    var a   = c.az * Math.PI / 180;
    var d   = Math.max(0, Math.min(1, c.d));
    var px  = cx + Math.sin(a) * d * R;
    var py  = cy - Math.cos(a) * d * R;
    var col = RWR_COL[c.tr] || RWR_COL[0];
    var isLock = c.tr === 2;
    var s   = 17;
    var op  = (typeof c.fr === 'number' ? Math.max(0, Math.min(1, c.fr)) : 1);
    out += '<g opacity="' + op.toFixed(3) + '">';
    out += '<polygon points="' + px + ',' + (py - s) + ' ' + (px + s) + ',' + py + ' ' +
           px + ',' + (py + s) + ' ' + (px - s) + ',' + py + '" fill="' + col + '"/>';
    if (isLock) {
      var b = 31, t = 11;
      out += '<g fill="none" stroke="' + col + '" stroke-width="4">';
      out += '<path d="M' + (px - b) + ' ' + (py - b + t) + ' V' + (py - b) + ' H' + (px - b + t) + '"/>';
      out += '<path d="M' + (px + b - t) + ' ' + (py - b) + ' H' + (px + b) + ' V' + (py - b + t) + '"/>';
      out += '<path d="M' + (px + b) + ' ' + (py + b - t) + ' V' + (py + b) + ' H' + (px + b - t) + '"/>';
      out += '<path d="M' + (px - b + t) + ' ' + (py + b) + ' H' + (px - b) + ' V' + (py + b - t) + '"/>';
      out += '</g>';
    }
    var right = px >= cx;
    var lx = px + (right ? 26 : -26);
    out += '<text x="' + lx.toFixed(1) + '" y="' + (py + 10).toFixed(1) + '" fill="' + col +
           '" text-anchor="' + (right ? 'start' : 'end') + '">' + rwrShort(c.n) + '</text>';
    out += '</g>';
  });
  g.innerHTML = out;
}
// Incoming missiles: { az, rng (km), st (seeker) }. A flickering spear (currentColor +
// the #rwr-threats CSS animation) points from the missile's bearing in toward the player.
var mwItems = [];
function renderThreats() {
  var g = document.getElementById('rwr-threats');
  if (!g) return;
  var cx = 500, cy = 500, R = 460, RIN = 60, RMAX = 6, out = '';   // RMAX = km mapped to the rim
  (mwItems || []).forEach(function(m) {
    var a = m.az * Math.PI / 180, sn = Math.sin(a), cs = Math.cos(a);
    // Notch line (radar seekers): static dashed-yellow beam axis (diameter) through the player.
    if (typeof m.nb === 'number') {
      var na = m.nb * Math.PI / 180, ns = Math.sin(na), nc = Math.cos(na);
      out += '<line x1="' + (cx + ns * R).toFixed(1) + '" y1="' + (cy - nc * R).toFixed(1) +
             '" x2="' + (cx - ns * R).toFixed(1) + '" y2="' + (cy + nc * R).toFixed(1) +
             '" stroke="#ffd21e" stroke-width="3" stroke-dasharray="14 12"/>';
    }
    // Missile at a proximity radius (closer -> nearer centre), so the line shortens as it closes.
    var frac = Math.max(0, Math.min(1, (typeof m.rng === 'number' ? m.rng : RMAX) / RMAX));
    var tr = RIN + 35 + frac * (R - (RIN + 35));
    var mx = cx + sn * tr,  my = cy - cs * tr;
    var ix = cx + sn * RIN, iy = cy - cs * RIN;
    out += '<line x1="' + mx.toFixed(1) + '" y1="' + my.toFixed(1) + '" x2="' + ix.toFixed(1) +
           '" y2="' + iy.toFixed(1) + '" stroke="#ff3b30" stroke-width="3" stroke-linecap="round"/>';
    var ux = -sn, uy = cs, qx = cs, qy = sn, HL = 36, HB = 8, HW = 10;   // slender dart (currentColor, flickers)
    out += '<polygon points="' + (mx + ux * HL).toFixed(1) + ',' + (my + uy * HL).toFixed(1) + ' ' +
           (mx - ux * HB + qx * HW).toFixed(1) + ',' + (my - uy * HB + qy * HW).toFixed(1) + ' ' +
           (mx - ux * HB - qx * HW).toFixed(1) + ',' + (my - uy * HB - qy * HW).toFixed(1) + '" fill="currentColor"/>';
    var lr = tr + 34, lx = cx + sn * lr, ly = cy - cs * lr;
    var label = (m.st ? m.st + ' ' : '') + (typeof m.rng === 'number' ? m.rng.toFixed(1) : '');
    out += '<text x="' + lx.toFixed(1) + '" y="' + (ly + 10).toFixed(1) + '" fill="#ff3b30" text-anchor="' +
           (sn >= 0 ? 'start' : 'end') + '">' + label + '</text>';
  });
  g.innerHTML = out;
}
window.addEventListener('message', function(e) {
  var m = e.data;
  if (!m || !m.mfd) return;
  if (m.type === 'rwr') { rwrItems = Array.isArray(m.items) ? m.items : []; renderRwr(); }
  else if (m.type === 'mw') { mwItems = Array.isArray(m.items) ? m.items : []; renderThreats(); }
});
// Flicker the missile layer red<->yellow on its own timer (~3.8 Hz), independent of the data
// rate; only writes when a missile is present (children use currentColor).
var mwFlip = false;
setInterval(function() {
  var g = document.getElementById('rwr-threats');
  if (!g || !g.firstChild) return;
  mwFlip = !mwFlip;
  g.style.color = mwFlip ? '#ffd21e' : '#ff3b30';
}, 130);
renderRwr();
renderThreats();
