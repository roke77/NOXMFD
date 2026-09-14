// Opt-in preview check; NODE_PATH must resolve Playwright. This uses a static image, not MJPEG.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    let configDocuments = 0;
    page.on('request', r => { if (new URL(r.url()).pathname === '/tgpcfg') configDocuments++; });
    page.on('pageerror', e => errors.push(e.message));
    await page.goto('http://127.0.0.1:8782/');
    await page.waitForFunction(() => typeof showPage === 'function');
    await page.evaluate(() => showPage('tgp'));
    await page.frameLocator('#page-frame').locator('#tgp-img').waitFor({ state: 'attached' });
    const tgpFrame = page.frames().find(f => /\/tgp$/.test(f.url()));
    await tgpFrame.waitForFunction(() => typeof tgpTornDown !== 'undefined');
    await page.waitForTimeout(1500);
    await tgpFrame.evaluate(() => { window.documentIdentity = Math.random(); });
    const identity = await tgpFrame.evaluate(() => window.documentIdentity);
    for (let i = 0; i < 30; i++) {
      await page.evaluate(() => showPage('tgp'));
      const frame = page.frameLocator('#page-frame');
      await frame.locator('#tgp-img').waitFor({ state: 'attached' });
      await page.waitForTimeout(100);
      await page.evaluate(() => showPage('tgpcfg'));
      await frame.locator('#tcfg-panel').waitFor({ state: 'visible' });
      await tgpFrame.waitForFunction(() => tgpTornDown && !tgpImg.hasAttribute('src'));
      assert.equal(await tgpFrame.evaluate(() => window.documentIdentity), identity, 'document reused at cycle ' + i);
    }
    await page.evaluate(() => showPage('tgp'));
    await page.frameLocator('#page-frame').locator('#tgp-img').waitFor({ state: 'attached' });
    await tgpFrame.waitForFunction(() => !tgpTornDown);
    const frame = tgpFrame;
    await frame.waitForFunction(() => typeof overlayObserver !== 'undefined');
    const result = await frame.evaluate(() => {
      window.dispatchEvent(new Event('pagehide'));
      window.dispatchEvent(new Event('pagehide'));
      return { src: tgpImg.getAttribute('src'), stopped: tgpTornDown, timer: joystickKeepalive };
    });
    assert.deepEqual(result, { src: null, stopped: true, timer: null });
    await frame.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true })));
    assert.equal(await frame.evaluate(() => tgpTornDown), false);
    assert.equal(configDocuments, 1, 'configuration markup loads once across 30 switches');
    await page.evaluate(() => setSplit('h'));
    await page.evaluate(() => paneNavigate(0, 'tgp'));
    const split = page.frames().find(f => f.url().includes('/tgp?bare'));
    await split.waitForFunction(() => typeof tgpTornDown !== 'undefined');
    await split.evaluate(() => { window.splitIdentity = 42; });
    for (let i = 0; i < 5; i++) {
      await page.evaluate(() => paneNavigate(0, 'tgpcfg'));
      await split.waitForFunction(() => tgpTornDown);
      await page.evaluate(() => paneNavigate(0, 'tgp'));
      await split.waitForFunction(() => !tgpTornDown);
      assert.equal(await split.evaluate(() => window.splitIdentity), 42);
    }
    assert.deepEqual(errors, []);
    console.log('TGP lifecycle: 30 static-preview navigation cycles and restoration passed');
  } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
