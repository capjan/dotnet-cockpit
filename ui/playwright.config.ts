import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  use: { baseURL: 'http://127.0.0.1:5230', colorScheme: 'light', trace: 'retain-on-failure' },
  webServer: {
    command: '../artifacts/native/dotnet-cockpit --no-browser --port 5230',
    url: 'http://127.0.0.1:5230/api/health',
    reuseExistingServer: !process.env.CI,
    timeout: 30_000,
  },
});
