import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { get as httpGet } from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const executable = resolve(process.argv[2] ?? 'artifacts/native/dotnet-cockpit');
const native = !process.argv.includes('--tool');
const sdkOutput = spawnSync('dotnet', ['--list-sdks'], { encoding: 'utf8' });
const runtimeOutput = spawnSync('dotnet', ['--list-runtimes'], { encoding: 'utf8' });
assert.equal(sdkOutput.status, 0, 'The smoke-test runner needs an SDK for the reference inventory.');
assert.equal(runtimeOutput.status, 0);
const sdks = [...sdkOutput.stdout.matchAll(/^(\S+)\s+\[(.+)\]\s*$/gm)].map(([, version, path]) => ({ version, path }));
const runtimes = [...runtimeOutput.stdout.matchAll(/^(\S+)\s+(\S+)\s+\[(.+)\]\s*$/gm)].map(([, name, version, path]) => ({ name, version, path }));

async function runWithoutRepositoryFiles(environment, verify) {
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
    assert.equal(page.status, 200);
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
    assert.equal(response.status, 200);
    await verify(await response.json());
  } finally {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill();
      await once(child, 'exit');
    }
    await rm(directory, { recursive: true, force: true });
  }
}

await runWithoutRepositoryFiles(process.env, data => {
  assert.equal(data.status, 'ready');
  assert.deepEqual(data.sdks, sdks, 'SDKs must match the real dotnet CLI inventory.');
  assert.deepEqual(data.runtimes, runtimes, 'Runtimes must match the real dotnet CLI inventory.');
  assert.ok(data.executablePath);
  assert.ok(data.operatingSystem);
  assert.ok(data.architecture);
  assert.ok(Number.isFinite(Date.parse(data.checkedAt)));
});
console.log('PASS: embedded UI, health endpoint, loopback host validation, and real .NET inventory.');

if (native) {
  await runWithoutRepositoryFiles({ ...process.env, PATH: '' }, data => {
    assert.equal(data.status, 'notFound');
    assert.equal(data.executablePath, null);
    assert.deepEqual(data.sdks, []);
    assert.deepEqual(data.runtimes, []);
  });
  console.log('PASS: native executable starts and serves its UI without dotnet on PATH.');
}
