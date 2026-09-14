// Opt-in preview check; NODE_PATH must resolve Playwright. This uses a static image, not MJPEG.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('http://127.0.0.1:8782/');
    await page.waitForFunction(() => typeof showPage === 'function');
    for (let i = 0; i < 30; i++) {
      await page.evaluate(() => showPage('tgp'));
      const frame = page.frameLocator('#page-frame');
      await frame.locator('#tgp-img').waitFor({ state: 'attached' });
      await page.waitForTimeout(100);
      await page.evaluate(() => showPage('tgpcfg'));
      await frame.locator('#tgp-img').waitFor({ state: 'detached' });
    }
    await page.evaluate(() => showPage('tgp'));
    await page.frameLocator('#page-frame').locator('#tgp-img').waitFor({ state: 'attached' });
    const frame = page.frames().find(f => /\/tgp(?:\?|$)/.test(f.url()));
    await frame.waitForFunction(() => typeof overlayObserver !== 'undefined');
    const result = await frame.evaluate(() => {
      window.dispatchEvent(new Event('pagehide'));
      window.dispatchEvent(new Event('pagehide'));
      return { src: tgpImg.getAttribute('src'), stopped: tgpTornDown, timer: joystickKeepalive };
    });
    assert.deepEqual(result, { src: null, stopped: true, timer: null });
    await frame.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true })));
    assert.equal(await frame.evaluate(() => tgpTornDown), false);
    assert.deepEqual(errors, []);
    console.log('TGP lifecycle: 30 static-preview navigation cycles and restoration passed');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
