import { expect, test } from '@playwright/test';

const installation = {
  status: 'ready', executablePath: '/usr/share/dotnet/dotnet', operatingSystem: 'Linux', architecture: 'x64', checkedAt: '2026-10-28T10:00:00Z', error: null,
  sdks: [{ version: '10.0.100', path: '/usr/share/dotnet/sdk' }, { version: '9.0.100', path: '/usr/share/dotnet/sdk' }],
  runtimes: [{ name: 'Microsoft.NETCore.App', version: '10.0.0', path: '/usr/share/dotnet/shared/Microsoft.NETCore.App' }],
};

test('shows real installations from the native backend', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Your .NET environment' })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Installed .NET versions' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: '.NET SDK' }).first()).toBeVisible();
});

test('filters by type and version and switches themes with the keyboard', async ({ page }) => {
  await page.route('**/api/installation', route => route.fulfill({ json: installation }));
  await page.goto('/');
  await page.getByRole('tab', { name: 'SDKs', exact: true }).click();
  await expect(page.getByRole('row')).toHaveCount(3);
  await page.getByRole('textbox', { name: 'Filter installed versions' }).fill('10.0.100');
  await expect(page.getByRole('row')).toHaveCount(2);
  await expect(page.getByRole('row').last()).toContainText('10.0.100');
  await page.getByRole('textbox', { name: 'Filter installed versions' }).fill('no-match');
  await expect(page.getByRole('heading', { name: 'No matching versions' })).toBeVisible();
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page.getByRole('row')).toHaveCount(4);
  const theme = page.getByRole('button', { name: 'Switch to dark mode' });
  await theme.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('[data-theme="dark"]')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Switch to light mode' })).toBeFocused();
});

test('refreshes inventory without a page reload', async ({ page }) => {
  let refreshed = false;
  await page.route('**/api/installation', route => route.fulfill({ json: refreshed
    ? { ...installation, sdks: [...installation.sdks, { version: '11.0.100-preview.1', path: '/usr/share/dotnet/sdk' }] }
    : installation }));
  await page.goto('/');
  await expect(page.getByRole('row')).toHaveCount(4);
  refreshed = true;
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(page.getByRole('row')).toHaveCount(5);
  await expect(page.getByRole('row').filter({ hasText: '11.0.100-preview.1' })).toBeVisible();
});

test('explains an absent installation on a narrow screen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route('**/api/installation', route => route.fulfill({ json: { ...installation, status: 'notFound', executablePath: null, sdks: [], runtimes: [] } }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'No .NET installation found' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

test('shows a query error and can recover', async ({ page }) => {
  let recovered = false;
  await page.route('**/api/installation', route => recovered
    ? route.fulfill({ json: installation })
    : route.fulfill({ status: 503, json: { ...installation, status: 'error', error: 'The installation could not be read.' } }));
  await page.goto('/');
  await expect(page.getByText('The installation could not be read.', { exact: false })).toBeVisible();
  recovered = true;
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByRole('table')).toBeVisible();
});
