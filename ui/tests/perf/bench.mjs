// Measures what a person feels, against the native AOT binary.
//   npm --prefix ui run bench -- [--binary path] [--runs 15] [--json file] [--check]
// Budgets follow the usual perception limits: under 100 ms feels instant, under 16 ms is one frame.
import { chromium } from '@playwright/test';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../..', import.meta.url));
const arg = (name, fallback) => {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
};
const binary = resolve(arg('binary', join(root, 'artifacts/native/dotnet-cockpit')));
const runs = Number(arg('runs', '15'));
const jsonOut = arg('json', null);
const check = process.argv.includes('--check');
const port = 5310;
const base = `http://127.0.0.1:${port}`;
const settle = ms => new Promise(done => setTimeout(done, ms));

const BUDGET = { start: 500, cold: 500, nav: 100, input: 50, ttfb: 50 };
const results = [];
function record(group, name, samples, budget) {
  const sorted = [...samples].sort((a, b) => a - b);
  const pick = q => sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))];
  results.push({ group, name, n: sorted.length, p50: Math.round(pick(0.5)), p95: Math.round(pick(0.95)), budget });
}

async function startServer(env = {}) {
  const startedAt = performance.now();
  const child = spawn(binary, ['--no-browser', '--port', String(port)], {
    cwd: root,
    env: { ...process.env, DOTNET_COCKPIT_CONFIG_DIR: configDir, ...env },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  servers.add(child);
  for (;;) {
    try {
      const response = await fetch(`${base}/api/health`);
      if (response.ok) break;
    } catch { /* not listening yet */ }
    await settle(2);
  }
  return { child, listening: performance.now() - startedAt, startedAt };
}

async function stopServer(child) {
  if (child.exitCode !== null) throw new Error(`The server exited early with code ${child.exitCode}. Is port ${port} still in use?`);
  child.kill();
  await new Promise(done => child.once('exit', done));
}

const configDir = mkdtempSync(join(tmpdir(), 'cockpit-bench-'));

// Event Timing reports what the browser itself saw: input delay, handlers and the next paint.
const installObserver = () => {
  window.__events = [];
  new PerformanceObserver(list => {
    for (const entry of list.getEntries()) window.__events.push(entry.duration);
  }).observe({ type: 'event', durationThreshold: 16, buffered: true });
};

async function nextFrame(page) {
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
}

async function paintTime(page) {
  return page.evaluate(() => new Promise(done => {
    const read = () => {
      const paint = performance.getEntriesByName('first-contentful-paint')[0];
      if (paint) done(performance.timeOrigin + paint.startTime);
      else requestAnimationFrame(read);
    };
    read();
  }));
}

/** Time from a real click until the next frame, including the slowest browser-reported event. */
async function measureInput(page, name, act, undo, budget = BUDGET.input) {
  const samples = [];
  for (let run = 0; run < runs; run++) {
    await page.evaluate(() => { window.__events = []; });
    await page.evaluate(() => {
      window.__inputAt = null;
      addEventListener('pointerdown', () => { window.__inputAt ??= performance.now(); }, { capture: true, once: true });
      addEventListener('keydown', () => { window.__inputAt ??= performance.now(); }, { capture: true, once: true });
    });
    await act(run);
    const elapsed = await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => done(performance.now() - window.__inputAt)))));
    await page.waitForTimeout(40);
    const reported = await page.evaluate(() => Math.max(0, ...window.__events));
    samples.push(Math.max(reported, Math.min(elapsed, 2000)));
    if (undo) await undo(run);
    await page.waitForTimeout(30);
  }
  record('Eingabe', name, samples, budget);
}

const servers = new Set();
process.on('exit', () => { for (const child of servers) child.kill(); });
process.on('uncaughtException', error => { console.error(error); process.exit(1); });
process.on('unhandledRejection', error => { console.error(error); process.exit(1); });

const browser = await chromium.launch();

// ---- The very first launch has no stored catalog and depends on the network, so it is reported separately.
{
  const server = await startServer();
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  await page.goto(`${base}/overview`, { waitUntil: 'commit' });
  const painted = await paintTime(page);
  record('Start', 'Allererster Start, Katalog aus dem Netz', [painted - (performance.timeOrigin + server.startedAt)], 5000);
  await context.close();
  await settle(1500);
  await stopServer(server.child);
}

// ---- Launch: nothing cached in the browser, nothing in the server's memory
{
  const listening = [];
  const firstPaint = [];
  const firstUsable = [];
  for (let run = 0; run < Math.min(runs, 8); run++) {
    const server = await startServer();
    listening.push(server.listening);
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    await page.goto(`${base}/overview`, { waitUntil: 'commit' });
    const painted = await paintTime(page);
    await page.waitForFunction(() => document.documentElement.classList.contains('js'));
    const usable = await page.evaluate(() => performance.timeOrigin + performance.now());
    const zero = performance.timeOrigin + server.startedAt;
    firstPaint.push(painted - zero);
    firstUsable.push(usable - zero);
    await context.close();
    await stopServer(server.child);
  }
  record('Start', 'Prozessstart bis Server hört zu', listening, 100);
  record('Start', 'Prozessstart bis erste Seite sichtbar', firstPaint, BUDGET.cold);
  record('Start', 'Prozessstart bis Seite bedienbar', firstUsable, BUDGET.cold);
}

const server = await startServer();
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, colorScheme: 'dark' });
await context.addInitScript(installObserver);
const page = await context.newPage();

// ---- Server response, once warm
const routes = { Overview: '/overview', Versions: '/', Tools: '/tools', Settings: '/setup' };
{
  await page.goto(`${base}/overview`, { waitUntil: 'networkidle' });
  for (const [name, path] of Object.entries(routes)) {
    const samples = [];
    let size = 0;
    for (let run = 0; run < runs; run++) {
      const before = performance.now();
      const response = await fetch(base + path);
      samples.push(performance.now() - before);
      size = (await response.arrayBuffer()).byteLength;
    }
    record('Server', `${name}: Antwortzeit (${Math.round(size / 1024)} KB)`, samples, BUDGET.ttfb);
  }
}

// ---- Page changes: click until content is visible
{
  const links = [['Overview', 'a.nav-link[href="/overview"]'], ['Versions', 'a.nav-link[href="/"]'], ['Tools', 'a.nav-link[href="/tools"]'], ['Settings', 'a.nav-link[href="/setup"]']];
  const samples = Object.fromEntries(links.map(([name]) => [name, []]));
  await page.goto(`${base}/overview`, { waitUntil: 'networkidle' });
  for (let run = 0; run < runs; run++) {
    for (const [name, selector] of links) {
      if (page.url().endsWith(selector.match(/href="([^"]+)"/)[1])) continue;
      const box = await page.locator(selector).boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.waitForTimeout(150);
      const clickedAt = performance.timeOrigin + performance.now();
      await page.mouse.down();
      await page.mouse.up();
      await page.waitForURL(url => url.pathname === selector.match(/href="([^"]+)"/)[1], { waitUntil: 'commit' });
      const painted = await paintTime(page);
      samples[name].push(painted - clickedAt);
      await page.waitForLoadState('load');
    }
  }
  for (const [name, values] of Object.entries(samples)) record('Seitenwechsel', `Klick auf ${name} bis Inhalt sichtbar`, values, BUDGET.nav);
}

// ---- Interactions on an open page
{
  await page.goto(`${base}/`, { waitUntil: 'networkidle' });
  const inspect = page.locator('[data-inspect]').first();
  await measureInput(page, 'Versions: Drawer öffnen', () => inspect.click(), () => page.keyboard.press('Escape'));
  const seg = page.locator('[data-version-kind="sdks"]');
  await measureInput(page, 'Versions: Filter SDKs umschalten', () => seg.click(), () => seg.click());
  const chevron = page.locator('[data-collapse-channel]').first();
  await measureInput(page, 'Versions: Kanal einklappen', () => chevron.click(), () => chevron.click());
  const search = page.locator('#installed-version-filter');
  await search.focus();
  await measureInput(page, 'Versions: Tippen im Filter', run => page.keyboard.press(String(run % 10)), () => page.keyboard.press('Backspace'));
  await page.goto(`${base}/`, { waitUntil: 'networkidle' });
  const remove = page.locator('tr[data-kind] button.remove').first();
  if (await remove.count()) await measureInput(page, 'Versions: Rückfrage öffnen', () => remove.click(), () => page.locator('#confirm-dialog-cancel').click());
  const channel = page.locator('[data-remove-channel]').first();
  if (await channel.count()) await measureInput(page, 'Versions: Remove all öffnen', () => channel.click(), () => page.locator('#remove-dialog-cancel').click());
  const install = page.locator('#open-install-dotnet-btn');
  await measureInput(page, 'Versions: Install-Drawer öffnen', () => install.click(), () => page.keyboard.press('Escape'));
  await install.click();
  const view = page.locator('[data-install-lifecycle="all"]');
  const latest = page.locator('[data-install-lifecycle="latest"]');
  await measureInput(page, 'Install-Drawer: Filter wechseln', () => view.click(), () => latest.click());
  await page.keyboard.press('Escape');
  await measureInput(page, 'Palette öffnen', () => page.keyboard.press('Meta+k'), () => page.keyboard.press('Escape'));
  await page.keyboard.press('Meta+k');
  await page.waitForTimeout(600);
  await measureInput(page, 'Palette: Tippen', run => page.keyboard.press(String(run % 10)), () => page.keyboard.press('Backspace'));
  await page.keyboard.press('Escape');

  await page.goto(`${base}/tools`, { waitUntil: 'networkidle' });
  const toolInspect = page.locator('.tool-inspect-btn').first();
  await measureInput(page, 'Tools: Drawer öffnen', () => toolInspect.click(), () => page.keyboard.press('Escape'));
  const menu = page.locator('[data-tool-menu-btn]').first();
  await measureInput(page, 'Tools: Menü öffnen', () => menu.click(), () => page.keyboard.press('Escape'));
  const toolFilter = page.locator('[data-tools-table-filter]');
  await toolFilter.focus();
  await measureInput(page, 'Tools: Tippen im Filter', run => page.keyboard.press(String(run % 10)), () => page.keyboard.press('Backspace'));
  const add = page.locator('#open-add-tool-btn');
  await measureInput(page, 'Tools: Add-Tool öffnen', () => add.click(), () => page.keyboard.press('Escape'));

  await page.goto(`${base}/setup`, { waitUntil: 'networkidle' });
  const light = page.locator('header [data-theme-pref="light"]');
  const dark = page.locator('header [data-theme-pref="dark"]');
  await measureInput(page, 'Settings: Theme wechseln', () => light.click(), () => dark.click());
  const github = page.locator('[data-palette-pref="github"]');
  const cockpit = page.locator('[data-palette-pref="cockpit"]');
  await measureInput(page, 'Settings: Palette wechseln', () => github.click(), () => cockpit.click());
}

// ---- Waiting for data: click or last key press until the result is on screen
{
  const refresh = [];
  for (let run = 0; run < runs; run++) {
    await page.goto(`${base}/`, { waitUntil: 'networkidle' });
    const clickedAt = performance.timeOrigin + performance.now();
    await page.locator('a.checked').click({ noWaitAfter: true });
    await page.waitForURL(url => url.search.includes('refresh='), { waitUntil: 'commit' });
    refresh.push(await paintTime(page) - clickedAt);
    await page.waitForLoadState('load');
  }
  record('Warten auf Daten', 'Refresh: Klick bis neuer Stand sichtbar', refresh, 300);

  const palette = [];
  for (let run = 0; run < runs; run++) {
    await page.goto(`${base}/overview`, { waitUntil: 'load' });
    await page.keyboard.press('Meta+k');
    const startedAt = performance.now();
    await page.locator('#cmdk-input').fill('10.0');
    await page.locator('#cmdk-dynamic-list .opt:not([hidden])').first().waitFor();
    palette.push(performance.now() - startedAt);
  }
  record('Warten auf Daten', 'Palette: erste Suche bis Treffer sichtbar', palette, 100);

  const queries = ['coverage', 'dotnet-ef', 'format', 'outdated', 'reportgenerator', 'trace'];
  const first = [];
  const again = [];
  const feedback = [];
  await page.goto(`${base}/tools`, { waitUntil: 'networkidle' });
  await page.locator('#open-add-tool-btn').click();
  const box = page.locator('#sheet-search-input');
  for (const query of queries.slice(0, Math.min(queries.length, runs))) {
    for (const bucket of [first, again]) {
      await box.fill('');
      await page.waitForTimeout(300);
      await box.focus();
      const startedAt = performance.now();
      await box.fill(query);
      if (bucket === first) {
        await page.locator('#sheet-results[aria-busy="true"], #sheet-results .empty-state').first().waitFor({ timeout: 1000 }).catch(() => {});
        feedback.push(performance.now() - startedAt);
      }
      await page.waitForFunction(q => [...document.querySelectorAll('#sheet-results [data-pkg-id]')].length > 0 && document.querySelector('#sheet-results').dataset.query === q, query, { timeout: 15000 });
      bucket.push(performance.now() - startedAt);
    }
  }
  record('Warten auf Daten', 'NuGet-Suche: Tippen bis sichtbare Rückmeldung', feedback, BUDGET.nav);
  record('Warten auf Daten', 'NuGet-Suche: neue Suche bis Ergebnis (Netz)', first, 1000);
  record('Warten auf Daten', 'NuGet-Suche: gleiche Suche erneut', again, BUDGET.nav);
}

await browser.close();
await stopServer(server.child);

// ---- Report
const size = statSync(binary).size;
console.log(`\nBinary: ${binary} (${(size / 1048576).toFixed(1)} MB), ${runs} Läufe\n`);
let failed = 0;
for (const group of [...new Set(results.map(result => result.group))]) {
  console.log(group);
  for (const { name, p50, p95, budget } of results.filter(result => result.group === group)) {
    const ok = p95 <= budget;
    if (!ok) failed++;
    console.log(`  ${ok ? 'OK  ' : 'ZU LANGSAM'.padEnd(4)} ${String(p50).padStart(6)} ms  p95 ${String(p95).padStart(6)} ms  Budget ${String(budget).padStart(4)}  ${name}`);
  }
}
console.log(`\n${results.length - failed} von ${results.length} im Budget`);
if (jsonOut) writeFileSync(jsonOut, JSON.stringify(results, null, 2));
if (check && failed) process.exit(1);
