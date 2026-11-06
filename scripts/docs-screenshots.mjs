// Capture documentation images from fixed inventory, never from a running user installation.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '../ui/node_modules/@playwright/test/index.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const fixtures = mkdtempSync(join(tmpdir(), 'cockpit-docs-'));
const env = { ...process.env, DOTNET_COCKPIT_FIXTURES: fixtures };
let server;
let browser;
try {
  const result = spawnSync('dotnet', ['test', 'DotnetCockpit.sln', '--filter', 'FullyQualifiedName~PageFixtureTests', '--nologo', '--verbosity', 'quiet'], { cwd: root, env, stdio: 'inherit' });
  if (result.status !== 0) throw new Error('Could not render documentation fixtures.');

  for (const file of readdirSync(fixtures)) {
    let html = readFileSync(join(fixtures, file), 'utf8');
    // PageLayout contains live process context even when inventory comes from fixtures.
    html = html.replace(/<div class="ws-card">[\s\S]*?<\/div>\s*<\/div>/, '<div class="ws-card"><div class="k">Workspace</div><b>sample-app</b><code><bdi>/Users/dev/sample-app</bdi></code></div>');
    html = html.replace(/<span class="muted text-xs">for [^<]*<\/span>/g, '<span class="muted text-xs">for dev</span>');
    html = html.replace(/<a class="checked"[\s\S]*?<\/a>/, '<a class="checked" href="?refresh=1" aria-label="Refresh">Checked 10:00</a>');
    html = html.replaceAll(homedir(), '/Users/dev');
    html = html.replaceAll('/Users/dev/project', '/Users/dev/sample-app');
    // Keep only the three reviewed pages; Settings embeds additional machine paths.
    if (!['mixed-overview.html', 'mixed-versions.html', 'mixed-tools.html'].includes(file)) continue;
    if (/\/Volumes\/|\/home\/|\/Users\/(?!dev\/)|[A-Z]:\\|for jan\b/i.test(html)) {
      throw new Error(`Unreviewed machine path or identity in ${file}`);
    }
    writeFileSync(join(fixtures, file), html);
  }

  const occupied = await fetch('http://127.0.0.1:5235/api/health').then(() => true, () => false);
  if (occupied) throw new Error('Port 5235 is already in use; refusing to capture another server.');
  server = spawn(process.execPath, ['ui/tests/visual/fixture-server.mjs'], { cwd: root, env, stdio: 'inherit' });
  let ready = false;
  for (let attempt = 0; attempt < 50; attempt++) {
    if (server.exitCode !== null) throw new Error('Fixture server exited; check whether port 5235 is in use.');
    try { ready = (await fetch('http://127.0.0.1:5235/api/health')).ok; } catch { /* Wait for startup. */ }
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw new Error('Fixture server did not start.');
  browser = await chromium.launch();
  for (const theme of ['light', 'dark']) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, colorScheme: theme, deviceScaleFactor: 1 });
    // Fixed fixtures need no network access. Block every non-local request.
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort());
    for (const name of ['overview', 'versions', 'tools']) {
      await page.setViewportSize({ width: 1440, height: name === 'tools' ? 650 : name === 'versions' ? 900 : 1000 });
      await page.goto(`http://127.0.0.1:5235/mixed-${name}`);
      await page.evaluate(value => { document.documentElement.dataset.theme = value; }, theme);
      await page.evaluate(() => document.fonts.ready);
      await page.screenshot({ path: join(root, `docs/assets/${name}-${theme}.png`), fullPage: true, animations: 'disabled' });
    }
    await page.close();
  }
} finally {
  await browser?.close();
  server?.kill();
  rmSync(fixtures, { recursive: true, force: true });
}
