import { defineConfig } from '@playwright/test';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// Tests must never touch the settings of the person running them.
const configDir = mkdtempSync(join(tmpdir(), 'cockpit-browser-config-'));
process.env.DOTNET_COCKPIT_CONFIG_DIR = configDir;

export default defineConfig({
  testDir: './tests',
  use: { baseURL: 'http://127.0.0.1:5231', colorScheme: 'light', trace: 'retain-on-failure' },
  webServer: [
    { command: 'node tests/empty-server.mjs 5231', url: 'http://127.0.0.1:5231/api/health', timeout: 30_000 },
    { command: '../artifacts/native/dotnet-cockpit --no-browser --port 5232', url: 'http://127.0.0.1:5232/api/health', env: { PATH: '', DOTNET_COCKPIT_CONFIG_DIR: configDir }, timeout: 30_000 },
    { command: 'node tests/tools-server.mjs', url: 'http://127.0.0.1:5234/api/health', timeout: 30_000 },
  ],
});
