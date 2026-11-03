import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = await mkdtemp(join(tmpdir(), 'cockpit-browser-tools-'));
await mkdir(join(directory, '.config'));
await writeFile(join(directory, '.config', 'dotnet-tools.json'), JSON.stringify({ version: 1, isRoot: true, tools: {
  'cockpit.fixture.tool': { version: '1.2.3', commands: ['fixture', 'fixture-alt'] },
} }));
const executable = fileURLToPath(new URL('../../artifacts/native/dotnet-cockpit', import.meta.url));
const pathEnv = process.env.PATH?.includes('.dotnet') ? process.env.PATH : `${process.env.HOME}/.dotnet:${process.env.PATH}`;
const child = spawn(executable, ['--no-browser', '--port', '5234'], { cwd: directory, stdio: 'inherit', env: { ...process.env, PATH: pathEnv } });
process.on('SIGTERM', () => child.kill());
process.on('SIGINT', () => child.kill());
child.on('exit', async code => {
  await rm(directory, { recursive: true, force: true });
  process.exit(code ?? 0);
});
