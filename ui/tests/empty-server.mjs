import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const directory = await mkdtemp(join(tmpdir(), 'cockpit-browser-empty-'));
const port = process.argv[2] ?? '5231';
const executable = fileURLToPath(new URL('../../artifacts/native/dotnet-cockpit', import.meta.url));
const child = spawn(executable, ['--no-browser', '--port', port], { cwd: directory, stdio: 'inherit' });
process.on('SIGTERM', () => child.kill());
process.on('SIGINT', () => child.kill());
child.on('exit', async code => {
  await rm(directory, { recursive: true, force: true });
  process.exit(code ?? 0);
});
