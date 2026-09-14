// Opt-in browser integration check: NODE_PATH must resolve Playwright; preview runs on 8782.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      window.mapDraws = 0;
      window.telemetryMessages = 0;
      window.addEventListener('message', e => {
        if (e.data && e.data.mfd && e.data.type === 'mapinfo') window.telemetryMessages++;
      });
      const draw = CanvasRenderingContext2D.prototype.clearRect;
      CanvasRenderingContext2D.prototype.clearRect = function (...args) {
        window.mapDraws++;
        return draw.apply(this, args);
      };
    });
    await page.goto('http://127.0.0.1:8782/');
    await page.waitForFunction(() => typeof showPage === 'function');
    await page.evaluate(() => showPage('map'));
    const map = page.frames().find(f => f.url().includes('/map-view'));
    await map.waitForFunction(() => window.mapDraws > 3);
    const identity = await map.evaluate(() => {
      window.lifecycleIdentity = Math.random();
      return window.lifecycleIdentity;
    });
    for (let i = 0; i < 10; i++) {
      await page.evaluate(() => showPage('wpt'));
      await page.waitForTimeout(100);
      const stopped = await map.evaluate(() => window.mapDraws);
      await page.waitForTimeout(250);
      assert.equal(await map.evaluate(() => window.mapDraws), stopped, 'hidden MAP must stop drawing');
      await page.evaluate(() => showPage('map'));
      await map.waitForFunction(n => window.mapDraws > n, stopped);
      assert.equal(await map.evaluate(() => window.lifecycleIdentity), identity, 'classic MAP must be reused');
    }
    await page.evaluate(() => setSplit('h'));
    for (let i = 0; i < 3; i++) {
      await page.evaluate(() => paneNavigate(0, 'map'));
      await page.waitForTimeout(200);
      await page.evaluate(() => paneNavigate(0, 'wpt'));
      await page.waitForTimeout(200);
    }
    await page.goto('http://127.0.0.1:8782/f35');
    await page.waitForTimeout(1500);
    const tap = page.frames().find(f => f.url().includes('telemetry=1'));
    assert.ok(tap, 'F-35 must mount the transport-only tap');
    assert.equal(await tap.locator('canvas, img').count(), 0, 'tap must own no render resources');
    assert.equal(await tap.evaluate(() => performance.getEntriesByType('resource').filter(r => new URL(r.name).pathname === '/map').length), 0);
    assert.ok(await page.evaluate(() => window.telemetryMessages > 0), 'transport-only tap must still feed the shell');
    await page.goto('http://127.0.0.1:8782/map-view?bare');
    await page.waitForFunction(() => window.mapDraws > 3);
    const disposed = await page.evaluate(() => {
      window.dispatchEvent(new Event('pagehide'));
      window.dispatchEvent(new Event('pagehide'));
      return { widths: Array.from(document.querySelectorAll('canvas'), c => c.width), draws: window.mapDraws };
    });
    assert.ok(disposed.widths.every(w => w === 0), 'disposal must release the canvas backing store');
    await page.waitForTimeout(200);
    assert.equal(await page.evaluate(() => window.mapDraws), disposed.draws, 'disposed MAP must not restart rendering');
    assert.deepEqual(errors, [], 'navigation must not throw');
    console.log('MAP lifecycle browser checks passed');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
