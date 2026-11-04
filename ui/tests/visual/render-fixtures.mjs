// Renders the fixture pages through the .NET test project, then runs the screenshot comparison.
import { spawnSync } from 'node:child_process';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../..', import.meta.url));
const pages = mkdtempSync(join(tmpdir(), 'cockpit-visual-'));
const env = { ...process.env, DOTNET_COCKPIT_FIXTURES: pages };
const run = (command, args, cwd) => {
  const result = spawnSync(command, args, { cwd, env, stdio: 'inherit', shell: process.platform === 'win32' });
  if (result.status !== 0) process.exit(result.status ?? 1);
};
run('dotnet', ['test', 'DotnetCockpit.sln', '--filter', 'FullyQualifiedName~PageFixtureTests', '--nologo', '--verbosity', 'quiet'], root);
run('npx', ['playwright', 'test', '--config', 'playwright.visual.config.mjs', ...process.argv.slice(2)], join(root, 'ui'));
