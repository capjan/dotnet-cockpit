import { defineConfig } from '@playwright/test';
import { mkdirSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

// Workers load this file again, so the directories are created once and passed on through the environment.
process.env.COCKPIT_E2E_DIR ||= mkdtempSync(join(tmpdir(), 'cockpit-e2e-'));
const root = process.env.COCKPIT_E2E_DIR;
const workspace = join(root, 'workspace');
mkdirSync(workspace, { recursive: true });
process.env.COCKPIT_E2E_WORKSPACE = workspace;
// A separate CLI home keeps global tools away from the machine's real ones, also when run locally.
process.env.DOTNET_CLI_HOME = join(root, 'cli-home');
process.env.DOTNET_COCKPIT_CONFIG_DIR = join(root, 'config');

const executable = process.platform === 'win32' ? 'dotnet-cockpit.exe' : 'dotnet-cockpit';

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '*.e2e.mjs',
  workers: 1,
  timeout: 10 * 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: { baseURL: 'http://127.0.0.1:5240', viewport: { width: 1440, height: 900 }, trace: 'retain-on-failure' },
  webServer: {
    command: `"${resolve('../artifacts/native', executable)}" --no-browser --port 5240`,
    cwd: workspace,
    url: 'http://127.0.0.1:5240/api/health',
    timeout: 60_000,
    stdout: 'pipe',
  },
});
