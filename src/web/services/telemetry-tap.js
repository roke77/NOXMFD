// The shell needs telemetry even when no visual MAP is mounted.
import { TelemetrySource } from './telemetry-source.js';

const source = new TelemetrySource();
source.connect();
window.addEventListener('pagehide', () => source.disconnect());
window.addEventListener('pageshow', (e) => { if (e.persisted) source.connect(); });
