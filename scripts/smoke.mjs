import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { get as httpGet } from 'node:http';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const executable = resolve(process.argv[2] ?? 'artifacts/native/dotnet-cockpit');
const native = !process.argv.includes('--tool');
const sdkOutput = spawnSync('dotnet', ['--list-sdks'], { encoding: 'utf8' });
const runtimeOutput = spawnSync('dotnet', ['--list-runtimes'], { encoding: 'utf8' });
assert.equal(sdkOutput.status, 0, 'The smoke-test runner needs an SDK for the reference inventory.');
assert.equal(runtimeOutput.status, 0);
const sdks = [...sdkOutput.stdout.matchAll(/^(\S+)\s+\[(.+)\]\s*$/gm)].map(([, version, path]) => ({ version, path, installationPath: join(path, version) }));
const runtimes = [...runtimeOutput.stdout.matchAll(/^(\S+)\s+(\S+)\s+\[(.+)\]\s*$/gm)].map(([, name, version, path]) => ({ name, version, path, installationPath: join(path, version) }));
const globalOutput = spawnSync('dotnet', ['tool', 'list', '--global', '--format', 'json'], { encoding: 'utf8', env: { ...process.env, DOTNET_NOLOGO: '1' } });
assert.equal(globalOutput.status, 0);
const globalTools = JSON.parse(globalOutput.stdout).data.map(tool => ({ ...tool, manifest: null }));
const manifest = JSON.stringify({ version: 1, isRoot: true, tools: { 'cockpit.fixture.tool': { version: '1.2.3', commands: ['fixture', 'fixture-alt'] } } });

async function runWithoutRepositoryFiles(environment, verify, expectedStatus = 200, manifestContents = manifest) {
  const directory = await mkdtemp(join(tmpdir(), 'dotnet-cockpit-smoke-'));
  await mkdir(join(directory, '.config'));
  await writeFile(join(directory, '.config', 'dotnet-tools.json'), manifestContents);
  const child = spawn(executable, ['--no-browser', '--port', '0'], { cwd: directory, env: environment, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  try {
    const url = await new Promise((accept, reject) => {
      const timer = setTimeout(() => reject(new Error(`Startup timed out.\n${output}`)), 30_000);
      child.on('error', error => { clearTimeout(timer); reject(error); });
      child.on('exit', code => { clearTimeout(timer); reject(new Error(`Process exited with ${code}.\n${output}`)); });
      child.stderr.on('data', chunk => { output += chunk; });
      child.stdout.on('data', chunk => {
        output += chunk;
        const match = output.match(/\.NET Cockpit: (http:\/\/127\.0\.0\.1:\d+\/)/);
        if (match) { clearTimeout(timer); accept(match[1]); }
      });
    });
    const get = path => fetch(new URL(path, url), { signal: AbortSignal.timeout(20_000) });
    const health = await get('api/health');
    assert.equal(health.status, 200);
    assert.deepEqual(await health.json(), { status: 'ok' });
    const page = await get('');
    assert.equal(page.status, expectedStatus);
    const html = await page.text();
    assert.match(html, /<title>\.NET Cockpit<\/title>/);
    const assets = [...html.matchAll(/(?:src|href)="(\/assets\/[^"?#]+)"/g)].map(match => match[1]);
    assert.ok(assets.some(asset => asset.endsWith('.js')), 'An embedded UI script must be present.');
    assert.ok(assets.some(asset => asset.endsWith('.css')), 'An embedded stylesheet must be present.');
    for (const asset of new Set(assets)) {
      const response = await get(asset);
      assert.equal(response.status, 200, asset);
      assert.match(response.headers.get('content-type'), asset.endsWith('.js') ? /javascript/ : /css/);
      assert.ok((await response.text()).length > 0, asset);
    }
    assert.equal((await get('does-not-exist')).status, 404);
    const untrustedHostStatus = await new Promise((accept, reject) => {
      httpGet(url, { headers: { Host: 'untrusted.example' } }, response => {
        response.resume();
        accept(response.statusCode);
      }).on('error', reject);
    });
    assert.equal(untrustedHostStatus, 400);
    const response = await get('api/installation');
    assert.equal(response.status, expectedStatus);
    await verify(await response.json(), html, get, url);
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill();
      await once(child, 'exit');
    }
    await rm(directory, { recursive: true, force: true });
  }
}

await runWithoutRepositoryFiles(process.env, async (data, html, get, url) => {
  assert.equal(data.status, 'ready');
  assert.deepEqual(data.sdks, sdks, 'SDKs must match the real dotnet CLI inventory.');
  assert.deepEqual(data.runtimes, runtimes, 'Runtimes must match the real dotnet CLI inventory.');
  assert.ok(data.executablePath);
  assert.ok(data.operatingSystem);
  assert.ok(data.architecture);
  assert.ok(Number.isFinite(Date.parse(data.checkedAt)));
  assert.equal([...html.matchAll(/<tr data-kind=/g)].length, sdks.length + runtimes.length, 'The initial HTML must contain the complete inventory without executing JavaScript.');
  const filtered = await (await get('?kind=sdks')).text();
  assert.equal([...filtered.matchAll(/<tr data-kind="sdk"/g)].length, sdks.length);
  assert.ok(!filtered.includes('<tr data-kind="runtime"'), 'Filtering must happen on the server.');
  const toolsResponse = await get('api/tools');
  assert.equal(toolsResponse.status, 200);
  const tools = await toolsResponse.json();
  assert.equal(tools.status, 'ready');
  assert.deepEqual(tools.global.tools, globalTools, 'Global tools must match the real dotnet CLI inventory.');
  assert.deepEqual(tools.local.tools, [{ packageId: 'cockpit.fixture.tool', version: '1.2.3', commands: ['fixture', 'fixture-alt'], manifest: join(tools.workingDirectory, '.config', 'dotnet-tools.json') }]);
  assert.equal(tools.global.error, null);
  assert.equal(tools.local.error, null);
  const toolsPage = await get('tools');
  assert.equal(toolsPage.status, 200);
  const toolsHtml = await toolsPage.text();
  assert.ok(toolsHtml.includes('data-tool-scope="global"'));
  assert.ok(toolsHtml.includes('href="/tools?scope=local"'));
  assert.ok(!toolsHtml.includes('cockpit.fixture.tool'));
  const localToolsPage = await get('tools?scope=local');
  assert.equal(localToolsPage.status, 200);
  const localToolsHtml = await localToolsPage.text();
  assert.ok(localToolsHtml.includes('data-tool-scope="local"'));
  assert.ok(localToolsHtml.includes('cockpit.fixture.tool'));
  assert.ok(localToolsHtml.includes('Remove local tools'));
  const removeManifest = await fetch(new URL('tools/remove-manifest', url), {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ scope: 'local' }),
    redirect: 'manual',
  });
  assert.equal(removeManifest.status, 302);
  assert.match(removeManifest.headers.get('location'), /notice=removed/);
  const toolsAfterRemoval = await (await get('api/tools')).json();
  assert.equal(toolsAfterRemoval.localManifestInitialized, false);
  assert.equal(toolsAfterRemoval.localManifestPath, null);
  const invalidAction = await fetch(new URL('tools/install', url), {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ packageId: '', version: '', scope: 'global' }),
    redirect: 'manual',
  });
  assert.equal(invalidAction.status, 302);
  assert.match(invalidAction.headers.get('location'), /notice=failed/);
});
console.log('PASS: server-rendered installations, global/local tools, filtering, embedded assets, and loopback host validation.');

if (native) {
  await runWithoutRepositoryFiles({ ...process.env, PATH: '' }, async (data, html, get) => {
    assert.equal(data.status, 'notFound');
    assert.equal(data.executablePath, null);
    assert.deepEqual(data.sdks, []);
    assert.deepEqual(data.runtimes, []);
    assert.ok(html.includes('No .NET installation found'));
    assert.ok((await (await get('tools')).text()).includes('No .NET installation found'));
    assert.equal((await (await get('api/tools')).json()).status, 'notFound');
  });
  console.log('PASS: native executable starts and serves its UI without dotnet on PATH.');

  const brokenHost = await mkdtemp(join(tmpdir(), 'dotnet-cockpit-broken-host-'));
  try {
    await writeFile(join(brokenHost, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'invalid executable', { mode: 0o755 });
    await runWithoutRepositoryFiles({ ...process.env, PATH: brokenHost }, async (data, html, get) => {
      assert.equal(data.status, 'error');
      assert.ok(html.includes('Installation information is unavailable'));
      assert.equal((await get('tools')).status, 503);
      assert.equal((await get('api/tools')).status, 503);
    }, 503);
  } finally {
    await rm(brokenHost, { recursive: true, force: true });
  }
  console.log('PASS: a broken dotnet host returns an error page and HTTP 503 instead of an empty inventory.');

  await runWithoutRepositoryFiles(process.env, async (_, __, get) => {
    const tools = await (await get('api/tools')).json();
    assert.deepEqual(tools.global.tools, globalTools);
    assert.ok(tools.local.error);
    assert.ok((await (await get('tools?scope=local')).text()).includes('Local tools could not be read'));
  }, 200, '{ invalid manifest');
  console.log('PASS: a malformed local manifest does not hide the global tool inventory.');
}
