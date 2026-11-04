import { expect, test } from '@playwright/test';

test('shows the setup page on every platform', async ({ page }) => {
  await page.goto('/setup');
  await expect(page.getByRole('heading', { name: 'Settings', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Settings', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('link', { name: 'Microsoft documentation', exact: true })).toHaveAttribute('href', /learn\.microsoft\.com/);
  await expect(page.getByRole('heading', { name: 'General', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Privacy & network', exact: true })).toBeVisible();
  await expect(page.locator('#settings-privacy')).toContainText('builds.dotnet.microsoft.com');
  await expect(page.locator('#settings-privacy')).toContainText('api.nuget.org');
  await expect(page.locator('#settings-privacy')).toContainText('api.github.com');
});
