// MIS page — a read-only reactive replica of the game's mission-info panel, driven by the shell
// over postMessage (docs/md-pages.md). No interaction, no commands — pure render of the 'mis'
// block. See mis.html for the message contract.

const nameEl = document.getElementById('mis-name');
const timeEl = document.getElementById('mis-time');
const scoreEl = document.getElementById('mis-score');
const descEl = document.getElementById('mis-description');

const LEVEL_LABEL = ['Conventional', 'Tactical', 'Strategic'];

let state = { present: false, name: '', description: '', tod: 0, duration: 0, score: 0, level: 0 };

// Formats a fractional-hours value as HH:MM:SS, mirroring the game's own UnitConverter.TimeOfDay
// (both "Time" — tod, 0..24 — and "Duration" — duration seconds / 3600 — go through this in-game).
function fmtHms(hours) {
  let total = Math.max(0, Math.round(hours * 3600));
  const hh = Math.floor(total / 3600);
  total -= hh * 3600;
  const mm = Math.floor(total / 60);
  const ss = total - mm * 60;
  const pad = n => String(n).padStart(2, '0');
  return pad(hh) + ':' + pad(mm) + ':' + pad(ss);
}

function paint() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;

  nameEl.textContent = state.name || '—';
  timeEl.textContent = 'Time ' + fmtHms(state.tod) + '  --  Duration ' + fmtHms(state.duration / 3600);
  const level = LEVEL_LABEL[state.level] || LEVEL_LABEL[0];
  scoreEl.textContent = 'Score ' + (typeof state.score === 'number' ? state.score : 0).toFixed(1)
                       + '  --  ' + level + ' level';
  descEl.textContent = state.description || '';
}

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'mis') return;
  state = {
    present:     !!m.present,
    name:        m.name || '',
    description: m.description || '',
    tod:         typeof m.tod === 'number' ? m.tod : 0,
    duration:    typeof m.duration === 'number' ? m.duration : 0,
    score:       typeof m.score === 'number' ? m.score : 0,
    level:       typeof m.level === 'number' ? m.level : 0,
  };
  paint();
});

paint();   // initial paint — UNAVAILABLE until the first frame arrives
