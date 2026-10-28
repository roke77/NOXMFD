// The shell needs telemetry even when no visual MAP is mounted.
import { TelemetrySource } from './telemetry-source.js';

const source = new TelemetrySource();
source.connect();
window.addEventListener('pagehide', () => source.disconnect());
// The shell's boot catch-up for battle damage (docs/battle-damage.md), same as map.js answers it.
window.addEventListener('message', (e) => {
  if (e.data && e.data.mfd === true && e.data.action === 'sysdmg-request') source.rebroadcastSysdmg();
});
window.addEventListener('pageshow', (e) => { if (e.persisted) source.connect(); });
