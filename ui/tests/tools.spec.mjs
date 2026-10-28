import { expect, test } from '@playwright/test';

test('navigates to the tools page and lists both scopes without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  try {
    await page.goto('http://127.0.0.1:5234/');
    await page.getByRole('link', { name: 'Installations', exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: '.NET Tools', exact: true })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: '.NET Tools', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Global tools', exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Local tools', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Initialize local tools', exact: true })).toHaveCount(0);
    await expect(page.locator('a.tool-tab[href="/tools?scope=local"]')).toHaveAttribute('aria-selected', 'false');
    await expect(page.getByRole('link', { name: '.NET Tools', exact: true })).not.toHaveClass(/nav-child/);
    await expect(page.getByRole('link', { name: '.NET Tools', exact: true })).toHaveAttribute('aria-current', 'page');
    const response = await page.request.get('http://127.0.0.1:5234/api/tools');
    const tools = await response.json();
    await expect(page.locator('.working-directory')).toContainText(tools.workingDirectory);
    await expect(page.locator('tr[data-tool-scope="global"]')).toHaveCount(tools.global.tools.length);
    await page.locator('a.tool-tab[href="/tools?scope=local"]').click();
    await expect(page.getByRole('table', { name: 'Local tools' })).toContainText('cockpit.fixture.tool');
    await expect(page.getByRole('table', { name: 'Local tools' })).toContainText('fixture-alt');
    await expect(page.getByRole('table', { name: 'Local tools' })).toContainText('dotnet-tools.json');
    await expect(page.getByRole('button', { name: 'Uninstall', exact: true })).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Remove local tools', exact: true })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Search .NET tools' })).toBeVisible();
    await expect(page.locator('a.tool-tab[href="/tools?scope=local"]')).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByRole('link', { name: 'Refresh', exact: true })).toHaveAttribute('href', '/tools?scope=local');
    await page.getByRole('link', { name: 'Refresh', exact: true }).click();
    await expect(page.getByRole('table', { name: 'Local tools' })).toContainText('cockpit.fixture.tool');
  } finally { await context.close(); }
});

test('keeps tools navigation available on narrow screens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await page.getByRole('link', { name: '.NET Tools', exact: true }).click();
  await expect(page.getByRole('heading', { name: '.NET Tools', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Global tools', exact: true })).toBeVisible();
  await page.locator('a.tool-tab[href="/tools?scope=local"]').click();
  await expect(page.getByRole('heading', { name: 'No local tools initialized' })).toBeVisible();
  await expect(page.locator('#local-tools-panel').getByRole('button', { name: 'Initialize local tools', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
});

test('offers local manifest initialization when none exists', async ({ page }) => {
  await page.goto('/tools?scope=local');
  await expect(page.getByRole('heading', { name: 'No local tools initialized' })).toBeVisible();
  await expect(page.locator('#local-tools-panel').getByRole('button', { name: 'Initialize local tools', exact: true })).toBeVisible();
  await expect(page.locator('.tool-manifest-notice')).toHaveCount(0);
  await expect(page.getByText('Search results will be installed globally', { exact: false })).toHaveCount(0);
});

test('explains why tools cannot be listed without a dotnet host', async ({ page }) => {
  await page.goto('http://127.0.0.1:5232/tools');
  await expect(page.getByRole('heading', { name: 'No .NET installation found' })).toBeVisible();
  await expect(page.getByText('A .NET SDK is required to list tools.', { exact: false })).toBeVisible();
});
