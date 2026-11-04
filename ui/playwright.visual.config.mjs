import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/visual',
  testMatch: '*.visual.mjs',
  snapshotPathTemplate: '{testDir}/baselines/{platform}/{arg}{ext}',
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.005 } },
  use: { baseURL: 'http://127.0.0.1:5235' },
  webServer: { command: 'node tests/visual/fixture-server.mjs', url: 'http://127.0.0.1:5235/api/health', timeout: 30_000 },
});
