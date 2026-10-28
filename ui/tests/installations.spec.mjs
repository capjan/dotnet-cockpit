import { expect, test } from '@playwright/test';

test('receives the real inventory in the initial HTML with version-specific directories', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  const runtimeCards = new Set(snapshot.runtimes.map(runtime => runtime.version)).size;
  const response = await page.goto('/');
  const html = await response.text();
  expect(html).toContain('<tr data-kind="sdk">');
  await expect(page.getByRole('heading', { name: 'Your .NET environment' })).toBeVisible();
  await expect(page.locator('tbody tr')).toHaveCount(snapshot.sdks.length + runtimeCards);
  for (const item of [...snapshot.sdks, ...snapshot.runtimes]) {
    await expect(page.locator('.installation-path').filter({ hasText: item.installationPath })).toBeVisible();
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
    await expect(page.locator('tbody tr')).toHaveCount(snapshot.sdks.length);
    await page.getByRole('searchbox', { name: 'Filter installed versions' }).fill(snapshot.sdks[0].version);
    await page.getByRole('button', { name: 'Filter', exact: true }).click();
    await expect(page).toHaveURL(/q=/);
    await expect(page.locator('tbody tr').first()).toContainText(snapshot.sdks[0].version);
    await expect(page.locator('tbody tr[data-kind="runtime"]')).toHaveCount(0);
    await page.getByRole('columnheader', { name: /^Version/ }).getByRole('link').click();
    await expect(page).toHaveURL(/direction=asc/);
    await page.getByRole('searchbox', { name: 'Filter installed versions' }).fill('no-match');
    await page.getByRole('button', { name: 'Filter', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'No matching versions' })).toBeVisible();
    await page.getByRole('link', { name: 'Clear filters' }).click();
    await expect(page.locator('tbody tr')).toHaveCount(snapshot.sdks.length + runtimeCards);
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
  await page.goto('/');
  const before = await page.locator('time').getAttribute('datetime');
  await page.getByRole('link', { name: 'Refresh', exact: true }).click();
  await expect(page.locator('time')).not.toHaveAttribute('datetime', before);
  await expect(page.getByRole('table')).toBeVisible();
});

test('renders an absent installation on a narrow screen without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  try {
    await page.goto('http://127.0.0.1:5232/');
    await expect(page.getByRole('heading', { name: 'No .NET installation found' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  } finally { await context.close(); }
});
