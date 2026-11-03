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
const uninstallExecutable = process.platform === 'win32'
  ? join(process.env.ProgramFiles ?? '', 'dotnet-core-uninstall', 'dotnet-core-uninstall.exe')
  : '/usr/local/bin/dotnet-core-uninstall';
const uninstallOutput = spawnSync(uninstallExecutable, ['list'], { encoding: 'utf8' });
const uninstallList = uninstallOutput.status === 0 ? uninstallOutput.stdout : '';
const uninstallable = { sdks: new Set(), runtimes: new Set() };
let uninstallSection = '';
for (const line of uninstallList.split(/\r?\n/)) {
  const heading = line.trim().replace(/:$/, '');
  if (heading.includes('SDK') && heading.endsWith('s')) uninstallSection = 'sdks';
  else if (heading.includes('ASP.NET Core Runtime') && heading.endsWith('s')) uninstallSection = 'aspnet';
  else if (heading.includes('Windows Desktop Runtime') && heading.endsWith('s')) uninstallSection = 'windowsdesktop';
  else if (heading.includes('.NET Core Runtimes')) uninstallSection = 'core';
  else if (line.trimStart().startsWith('.') && line.trimEnd().endsWith(':')) uninstallSection = '';
  const match = line.match(/^\s*(\d+(?:\.\d+)*(?:[-+][^\s]+)?)\b/);
  if (match && uninstallSection) {
    if (uninstallSection === 'sdks') uninstallable.sdks.add(match[1]);
    if (['core', 'aspnet', 'windowsdesktop'].includes(uninstallSection)) {
      uninstallable.runtimes.add(match[1]);
    }
  }
}
const expectedSdks = uninstallList.length > 0 ? sdks.filter(sdk => uninstallable.sdks.has(sdk.version)) : sdks;
const expectedRuntimes = uninstallList.length > 0 ? runtimes.filter(runtime => uninstallable.runtimes.has(runtime.version)) : runtimes;
const runtimeCards = new Set(expectedRuntimes.map(runtime => runtime.version)).size;
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
      const contentType = asset.endsWith('.js') ? /javascript/ : asset.endsWith('.css') ? /css/ : /png/;
      assert.match(response.headers.get('content-type'), contentType);
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
  assert.deepEqual(data.sdks, expectedSdks, 'SDKs must match the selected inventory source.');
  assert.deepEqual(data.runtimes, expectedRuntimes, 'Runtimes must match the selected inventory source.');
  assert.ok(data.executablePath);
  assert.ok(data.operatingSystem);
  assert.ok(data.architecture);
  assert.ok(Number.isFinite(Date.parse(data.checkedAt)));
  assert.equal([...html.matchAll(/<tr data-kind=/g)].length, expectedSdks.length + runtimeCards, 'The initial HTML must contain the complete inventory without executing JavaScript.');
  const filtered = await (await get('?kind=sdks')).text();
  assert.equal([...filtered.matchAll(/<tr data-kind="sdk"/g)].length, expectedSdks.length);
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
  assert.ok(toolsHtml.includes('id="global-tools-panel"'));
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
  const setupPage = await get('setup');
  assert.equal(setupPage.status, 200);
  const setupHtml = await setupPage.text();
  assert.ok(setupHtml.includes('<h1>Setup</h1>'));
  assert.ok(setupHtml.includes('.NET Uninstall Tool'));
  assert.ok(setupHtml.includes('learn.microsoft.com/en-us/dotnet/core/additional-tools/uninstall-tool-overview'));
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
    if (uninstallOutput.status === 0) {
      assert.equal(data.status, 'ready');
      assert.equal(data.executablePath, null);
      assert.deepEqual(data.sdks.toSorted((left, right) => left.version.localeCompare(right.version)), expectedSdks.map(sdk => ({ version: sdk.version, path: '', installationPath: '' })).toSorted((left, right) => left.version.localeCompare(right.version)));
      assert.deepEqual(data.runtimes.map(runtime => runtime.version), [...uninstallable.runtimes]);
      assert.ok(data.runtimes.every(runtime => runtime.path === '' && runtime.installationPath === ''));
      assert.ok(html.includes('Versions reported by dotnet-core-uninstall'));
    } else {
      assert.equal(data.status, 'notFound');
      assert.equal(data.executablePath, null);
      assert.deepEqual(data.sdks, []);
      assert.deepEqual(data.runtimes, []);
      assert.ok(html.includes('No .NET installation found'));
    }
    assert.ok((await (await get('tools')).text()).includes('No .NET installation found'));
    assert.equal((await (await get('api/tools')).json()).status, 'notFound');
  });
  console.log('PASS: native executable starts and serves its UI without dotnet on PATH.');

  const brokenHost = await mkdtemp(join(tmpdir(), 'dotnet-cockpit-broken-host-'));
  try {
    await writeFile(join(brokenHost, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'invalid executable', { mode: 0o755 });
    await runWithoutRepositoryFiles({ ...process.env, PATH: brokenHost }, async (data, html, get) => {
      if (uninstallOutput.status === 0) {
        assert.equal(data.status, 'ready');
        assert.equal(data.executablePath, null);
        assert.deepEqual(data.sdks.toSorted((left, right) => left.version.localeCompare(right.version)), expectedSdks.map(sdk => ({ version: sdk.version, path: '', installationPath: '' })).toSorted((left, right) => left.version.localeCompare(right.version)));
      assert.deepEqual(data.runtimes.map(runtime => runtime.version), [...uninstallable.runtimes]);
      assert.ok(data.runtimes.every(runtime => runtime.path === '' && runtime.installationPath === ''));
        assert.ok(html.includes('Versions reported by dotnet-core-uninstall'));
        assert.equal((await get('tools')).status, 503);
        assert.equal((await get('api/tools')).status, 503);
      } else {
        assert.equal(data.status, 'error');
        assert.ok(html.includes('Installation information is unavailable'));
        assert.equal((await get('tools')).status, 503);
        assert.equal((await get('api/tools')).status, 503);
      }
    }, uninstallOutput.status === 0 ? 200 : 503);
  } finally {
    await rm(brokenHost, { recursive: true, force: true });
  }
  console.log('PASS: a broken dotnet host uses uninstall inventory when available and otherwise returns HTTP 503.');

  await runWithoutRepositoryFiles(process.env, async (_, __, get) => {
    const tools = await (await get('api/tools')).json();
    assert.deepEqual(tools.global.tools, globalTools);
    assert.ok(tools.local.error);
    assert.ok((await (await get('tools?scope=local')).text()).includes('Local tools could not be read'));
  }, 200, '{ invalid manifest');
  console.log('PASS: a malformed local manifest does not hide the global tool inventory.');
}
