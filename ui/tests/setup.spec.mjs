import { expect, test } from '@playwright/test';

test('shows the .NET Uninstall Tool setup area', async ({ page }) => {
  await page.goto('/setup');
  await expect(page.getByRole('heading', { name: 'Setup', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '.NET Uninstall Tool', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Setup', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('link', { name: 'Microsoft documentation', exact: true })).toHaveAttribute('href', /learn\.microsoft\.com/);
});
