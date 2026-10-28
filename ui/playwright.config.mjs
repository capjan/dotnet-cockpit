import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  use: { baseURL: 'http://127.0.0.1:5231', colorScheme: 'light', trace: 'retain-on-failure' },
  webServer: [
    { command: '../artifacts/native/dotnet-cockpit --no-browser --port 5231', url: 'http://127.0.0.1:5231/api/health', timeout: 30_000 },
    { command: '../artifacts/native/dotnet-cockpit --no-browser --port 5232', url: 'http://127.0.0.1:5232/api/health', env: { PATH: '' }, timeout: 30_000 },
  ],
});
