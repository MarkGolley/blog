// Run against a local blog: WORD_GARDEN_BASE_URL=http://localhost:5187
// Set PLAYWRIGHT_MODULE to an installed Playwright package if it is not on NODE_PATH.
const {chromium} = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
 const output = process.env.WORD_GARDEN_EVIDENCE || path.resolve('artifacts/word-garden');
 await fs.mkdir(output, {recursive:true});
 const browser = await chromium.launch({headless:true, ...(process.env.PLAYWRIGHT_CHANNEL ? {channel:process.env.PLAYWRIGHT_CHANNEL} : {})});
 try {
  const context = await browser.newContext({viewport:{width:1440,height:1100}, acceptDownloads:true});
  const page = await context.newPage();
  async function capture(name) {
   await page.evaluate(() => { document.documentElement.style.scrollBehavior = 'auto'; window.scrollTo(0,0); });
   await page.screenshot({path:path.join(output,name),fullPage:true});
  }
  async function lint() {
   if (process.env.AXE_SCRIPT) {
    await page.addScriptTag({path:process.env.AXE_SCRIPT});
    const results = await page.evaluate(async () => (await axe.run('#word-garden')).violations);
    assert.deepEqual(results.map(v => ({id:v.id, nodes:v.nodes.map(n => n.target)})),[]);
   }
   assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true);
  }
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('response', r => {if(r.status() >= 400) errors.push(`${r.status()} ${r.url()}`);});
  const url = `${process.env.WORD_GARDEN_BASE_URL || 'http://localhost:5187'}/projects/word-garden`;
  await page.goto(url);
  await page.locator('#wg-word').filter({hasText:'serendipity'}).waitFor();
  await capture('desktop.png');
  await lint();
  await page.locator('#wg-reveal').click();
  assert.equal(await page.locator('#wg-answer').isVisible(),true);
  await page.locator('[data-rating="good"]').click();
  assert.equal(await page.locator('#wg-due').textContent(),'11');
  await page.reload();
  await page.locator('#wg-word').filter({hasText:'resilient'}).waitFor();
  assert.equal(await page.locator('#wg-due').textContent(),'11');
  await page.locator('[data-mode="library"]').click();
  await page.locator('#wg-term').fill('test phrase');
  await page.locator('#wg-definition').fill('A personal definition.');
  await page.locator('#wg-hint').fill('Remember this cue.');
  await page.locator('#wg-add button').click();
  assert.equal(await page.locator('#wg-total').textContent(),'13');
  await page.locator('#wg-search').fill('test phrase');
  assert.equal(await page.locator('.wg-word-row').count(),1);
  const downloadEvent = page.waitForEvent('download');
  await page.locator('#wg-export').click();
  const download = await downloadEvent;
  const backupPath = path.join(output,'backup.json');
  await download.saveAs(backupPath);
  const backup = JSON.parse(await fs.readFile(backupPath,'utf8'));
  assert.equal(backup.words.length,13);
  page.once('dialog', d => d.accept());
  await page.locator('.wg-word-row button').click();
  assert.equal(await page.locator('#wg-total').textContent(),'12');
  page.once('dialog', d => d.accept());
  await page.locator('#wg-import').setInputFiles(backupPath);
  await page.waitForFunction(() => document.querySelector('#wg-total').textContent === '13');
  await page.locator('#wg-import').setInputFiles({name:'invalid.json',mimeType:'application/json',buffer:Buffer.from('{"version":1,"words":[null]}')});
  await page.locator('#wg-message').filter({hasText:'Import failed'}).waitFor();
  assert.equal(await page.locator('#wg-total').textContent(),'13');
  await page.locator('[data-mode="match"]').click();
  const {seeds} = await import('../MyBlog/wwwroot/js/word-garden-core.mjs');
  for (const word of [...seeds,{term:'test phrase',meaning:'A personal definition.'}]) {
   const term = page.getByRole('button',{name:word.term,exact:true});
   if (await term.count()) {
    await term.click();
    await page.getByRole('button',{name:word.meaning,exact:true}).click();
   }
  }
  assert.match(await page.locator('#wg-match-status').textContent(),/6 of 6 pairs found.*Round complete/);
  await capture('matching.png');
  await lint();
  await page.locator('[data-mode="review"]').click();
  await page.setViewportSize({width:390,height:844});
  await capture('mobile.png');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true);
  await page.locator('#wg-reveal').click();
  assert.equal(await page.locator('#wg-ratings').isVisible(),true);
  await capture('mobile-answer.png');
  await lint();
  await page.evaluate(() => document.documentElement.dataset.theme='dark');
  await capture('mobile-dark.png');
  await lint();
  await page.locator('[data-mode="library"]').click();
  await capture('mobile-library.png');
  await lint();
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true);
  // Empty and damaged storage must not silently reset a real collection.
  await page.evaluate(() => localStorage.setItem('myblog:word-garden:v1',JSON.stringify({version:1,words:[]})));
  await page.reload();
  await page.locator('#wg-word').filter({hasText:'Your garden starts here.'}).waitFor();
  await page.evaluate(() => localStorage.setItem('myblog:word-garden:v1','broken'));
  await page.reload();
  await page.locator('#wg-storage').waitFor();
  assert.equal(await page.evaluate(() => localStorage.getItem('myblog:word-garden:v1')),'broken');
  assert.deepEqual(errors,[]);
  await fs.writeFile(path.join(output,'verification.json'),JSON.stringify({passed:true,checks:['review and reload persistence','add/search/remove','export/import','invalid import unchanged','complete matching round','mobile overflow','empty storage','damaged storage preserved'],browserErrors:errors},null,2));
  console.log('WordGarden browser checks passed. Evidence: '+output);
 } finally {await browser.close();}
})().catch(e => {console.error(e);process.exitCode=1;});
