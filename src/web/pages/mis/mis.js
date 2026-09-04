// Read-only replica of the game's mission-info panel, driven by the shell over postMessage.
// No interaction, no commands.

const nameEl = document.getElementById('mis-name');
const timeEl = document.getElementById('mis-time');
const scoreEl = document.getElementById('mis-score');
const descEl = document.getElementById('mis-description');

const LEVEL_LABEL = ['Conventional', 'Tactical', 'Strategic'];

let state = { present: false, name: '', description: '', tod: 0, duration: 0, score: 0, level: 0 };

// Formats fractional hours as HH:MM:SS, matching the game's UnitConverter.TimeOfDay.
function fmtHms(hours) {
  let total = Math.max(0, Math.round(hours * 3600));
  const hh = Math.floor(total / 3600);
  total -= hh * 3600;
  const mm = Math.floor(total / 60);
  const ss = total - mm * 60;
  const pad = n => String(n).padStart(2, '0');
  return pad(hh) + ':' + pad(mm) + ':' + pad(ss);
}

// Every field is written every 'mis' message with no guard, even though tod/duration/score are
// formatted (fmtHms rounds to whole seconds; score to one decimal) so most 10 Hz ticks land on an
// unchanged displayed string despite the underlying value having moved slightly — comparing the
// formatted text, not the raw number, still catches those (docs/web-efficiency-audit.md finding 09).
let lastName = null, lastTime = null, lastScore = null, lastDesc = null;
function paint() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;

  const name = state.name || '—';
  if (name !== lastName) { lastName = name; nameEl.textContent = name; }

  const time = 'Time ' + fmtHms(state.tod) + '  --  Duration ' + fmtHms(state.duration / 3600);
  if (time !== lastTime) { lastTime = time; timeEl.textContent = time; }

  const level = LEVEL_LABEL[state.level] || LEVEL_LABEL[0];
  const score = 'Score ' + (typeof state.score === 'number' ? state.score : 0).toFixed(1)
              + '  --  ' + level + ' level';
  if (score !== lastScore) { lastScore = score; scoreEl.textContent = score; }

  const desc = state.description || '';
  if (desc !== lastDesc) { lastDesc = desc; descEl.textContent = desc; }
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

paint();   // starts in unavailable state until the first frame arrives
