import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { get as httpGet } from 'node:http';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
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

async function runWithoutRepositoryFiles(environment, verify, expectedStatus = 200) {
  const directory = await mkdtemp(join(tmpdir(), 'dotnet-cockpit-smoke-'));
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
    await verify(await response.json(), html, get);
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill();
      await once(child, 'exit');
    }
    await rm(directory, { recursive: true, force: true });
  }
}

await runWithoutRepositoryFiles(process.env, async (data, html, get) => {
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
});
console.log('PASS: server-rendered inventory, server-side filtering, embedded assets, health endpoint, and loopback host validation.');

if (native) {
  await runWithoutRepositoryFiles({ ...process.env, PATH: '' }, (data, html) => {
    assert.equal(data.status, 'notFound');
    assert.equal(data.executablePath, null);
    assert.deepEqual(data.sdks, []);
    assert.deepEqual(data.runtimes, []);
    assert.ok(html.includes('No .NET installation found'));
  });
  console.log('PASS: native executable starts and serves its UI without dotnet on PATH.');

  const brokenHost = await mkdtemp(join(tmpdir(), 'dotnet-cockpit-broken-host-'));
  try {
    await writeFile(join(brokenHost, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'), 'invalid executable', { mode: 0o755 });
    await runWithoutRepositoryFiles({ ...process.env, PATH: brokenHost }, (data, html) => {
      assert.equal(data.status, 'error');
      assert.ok(html.includes('Installation information is unavailable'));
    }, 503);
  } finally {
    await rm(brokenHost, { recursive: true, force: true });
  }
  console.log('PASS: a broken dotnet host returns an error page and HTTP 503 instead of an empty inventory.');
}
