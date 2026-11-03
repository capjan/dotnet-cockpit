import { expect, test } from '@playwright/test';

test('receives the real inventory in the initial HTML with version-specific directories', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  const runtimeCards = new Set(snapshot.runtimes.map(runtime => runtime.version)).size;
  const response = await page.goto('/');
  const html = await response.text();
  expect(html).toContain('<tr data-kind="sdk">');
  await expect(page.getByRole('heading', { name: 'Versions', level: 1 })).toBeVisible();
  await expect(page.locator('tbody tr[data-kind]')).toHaveCount(snapshot.sdks.length + runtimeCards);
  for (const item of [...snapshot.sdks, ...snapshot.runtimes]) {
    if (item.installationPath) {
      await expect(page.locator('.installation-path').filter({ hasText: item.installationPath })).toBeVisible();
    }
  }
});

test('filters and sorts on the server with JavaScript disabled', async ({ browser, baseURL, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  const runtimeCards = new Set(snapshot.runtimes.map(runtime => runtime.version)).size;
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  try {
    await page.goto(baseURL);
    await page.getByRole('navigation', { name: 'Installation type' }).getByRole('link', { name: 'SDKs', exact: true }).click();
    await expect(page.locator('tbody tr[data-kind]')).toHaveCount(snapshot.sdks.length);
    await page.getByRole('searchbox', { name: 'Filter installed versions' }).fill(snapshot.sdks[0].version);
    await page.getByRole('button', { name: 'Filter', exact: true }).click();
    await expect(page).toHaveURL(/q=/);
    await expect(page.locator('tbody tr[data-kind]').first()).toContainText(snapshot.sdks[0].version);
    await expect(page.locator('tbody tr[data-kind="runtime"]')).toHaveCount(0);
    await page.getByRole('columnheader', { name: /^Version/ }).getByRole('link').click();
    await expect(page).toHaveURL(/direction=asc/);
    await page.getByRole('searchbox', { name: 'Filter installed versions' }).fill('no-match');
    await page.getByRole('button', { name: 'Filter', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'No matching versions' })).toBeVisible();
    await page.getByRole('link', { name: 'Clear filters' }).click();
    await expect(page.locator('tbody tr[data-kind]')).toHaveCount(snapshot.sdks.length + runtimeCards);
  } finally { await context.close(); }
});

test('switches Fluent themes with the keyboard and retains the choice across navigation', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  const theme = page.getByRole('button', { name: 'Switch to dark mode' });
  await theme.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.getByRole('navigation', { name: 'Installation type' }).getByRole('link', { name: 'SDKs', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  expect(errors).toEqual([]);
});

test('refreshes the server-rendered inventory', async ({ page }) => {
  await page.goto('/?q=&kind=all&sort=version&direction=desc#inventory');
  const before = await page.locator('time').getAttribute('datetime');
  await page.getByRole('link', { name: 'Refresh', exact: true }).click();
  await expect(page.locator('time')).not.toHaveAttribute('datetime', before);
  await expect(page.getByRole('table')).toBeVisible();
});

test('renders the no-host installation view on a narrow screen without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  try {
    const response = await page.goto('http://127.0.0.1:5232/');
    const html = await response.text();
    if (html.includes('No .NET installation found')) {
      await expect(page.getByRole('heading', { name: 'No .NET installation found' })).toBeVisible();
    } else {
      await expect(page.getByRole('heading', { name: 'Versions', level: 1 })).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Installed versions' })).toBeVisible();
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  } finally { await context.close(); }
});

test('filters instantly with client-side JavaScript without page reload', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  const runtimeCards = new Set(snapshot.runtimes.map(runtime => runtime.version)).size;
  const totalRows = snapshot.sdks.length + runtimeCards;
  await page.goto('/');

  const searchBox = page.getByRole('searchbox', { name: 'Filter installed versions' });
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(totalRows);

  await searchBox.fill(snapshot.sdks[0].version);
  await expect(page.locator('tbody tr[data-kind]:not([hidden])').first()).toContainText(snapshot.sdks[0].version);

  await searchBox.fill('xyz-nonexistent-query');
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'No matching versions' })).toBeVisible();

  await searchBox.fill('');
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(totalRows);
});

test('opens command palette with keyboard shortcut and triggers navigation', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#cmdk-dialog')).toBeHidden();
  await page.keyboard.press('Meta+k');
  await expect(page.locator('#cmdk-dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.locator('#cmdk-dialog')).toBeHidden();
});

test('renders pre-flight check with live diagnostic steps and trust policies', async ({ page }) => {
  await page.goto('/preflight');
  await expect(page.getByRole('heading', { name: 'Pre-flight check' })).toBeVisible();
  await expect(page.locator('.step')).toHaveCount(6);
  await expect(page.locator('.step').first()).toContainText('dotnet host');
  await expect(page.locator('.trust')).toContainText('127.0.0.1');
  await expect(page.locator('.trust')).toContainText('No telemetry');
  await page.getByRole('link', { name: /Open cockpit/i }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: 'Versions', level: 1 })).toBeVisible();
});

test('opens detail drawer when inspecting a version and displays equivalent command', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  await page.goto('/');

  const firstSdk = snapshot.sdks[0];
  await expect(page.locator('#version-drawer')).toBeHidden();

  // Click on the first row to inspect it
  await page.locator(`tbody tr[data-kind="sdk"]`).first().click();
  await expect(page.locator('#version-drawer')).toBeVisible();
  await expect(page.locator('#drawer-title')).toHaveText(firstSdk.version);
  await expect(page.locator('#drawer-command')).toContainText(`dotnet-core-uninstall remove ${firstSdk.version}`);

  // Close drawer
  await page.locator('#drawer-close').click();
  await expect(page.locator('#version-drawer')).toBeHidden();
});

test('renders the overview page with system summary and health cards', async ({ page }) => {
  await page.goto('/overview');
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.getByText('Installed SDKs', { exact: true })).toBeVisible();
  await expect(page.getByText('System Health')).toBeVisible();
  await expect(page.getByText('Support Lifecycle Timeline')).toBeVisible();
});
