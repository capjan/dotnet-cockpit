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

test('filters installed local tools instantly with client-side JavaScript', async ({ page }) => {
  await page.goto('http://127.0.0.1:5234/tools?scope=local');
  await expect(page.locator('tr[data-tool-scope="local"]')).toHaveCount(1);
  const filterInput = page.locator('input[data-tools-table-filter]');
  await expect(filterInput).toBeVisible();

  await filterInput.fill('fixture');
  await expect(page.locator('tr[data-tool-scope="local"]:not([hidden])')).toHaveCount(1);

  await filterInput.fill('no-matching-tool');
  await expect(page.locator('tr[data-tool-scope="local"]:not([hidden])')).toHaveCount(0);
  await expect(page.locator('.client-tools-empty')).toBeVisible();

  await filterInput.fill('');
  await expect(page.locator('tr[data-tool-scope="local"]:not([hidden])')).toHaveCount(1);
  await expect(page.locator('.client-tools-empty')).toBeHidden();
});

test('opens the NuGet Add Tool sheet and updates CLI preview', async ({ page }) => {
  await page.goto('http://127.0.0.1:5234/tools');
  const openBtn = page.locator('#open-add-tool-btn');
  await expect(openBtn).toBeVisible();
  await openBtn.click();
  const dialog = page.locator('#add-tool-dialog');
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Add a .NET tool' })).toBeVisible();
  await expect(page.locator('[data-sheet-scope="global"]')).toContainText('Global');
  await expect(page.locator('[data-sheet-scope="global"]')).toContainText('User');
  await expect(page.locator('[data-sheet-scope="local"]')).toContainText('Local');
  await expect(page.locator('[data-sheet-scope="local"]')).toContainText('Workspace');
  await expect(page.locator('#sheet-cli-preview')).toContainText('dotnet tool install --global');
  await page.locator('[data-sheet-scope="local"]').click();
  await expect(page.locator('#sheet-cli-preview')).toContainText('dotnet tool install --local');
  await page.locator('#close-add-tool-btn').click();
  await expect(dialog).toBeHidden();
});

test('displays tool logos, descriptions, and download counts and opens the tool detail drawer', async ({ page }) => {
  await page.goto('http://127.0.0.1:5234/tools?scope=local');
  const row = page.locator('tr[data-tool-scope="local"]').first();
  await expect(row.locator('.tool-logo.nuget-default-logo svg.nuget-logo-svg')).toBeVisible();
  await expect(row.locator('.tool-desc')).toContainText('Workspace verification fixture tool');
  await expect(row.locator('.tool-downloads-chip')).toContainText('↓');

  const drawer = page.locator('#tool-drawer');
  await expect(drawer).toBeHidden();

  await row.click();
  await expect(drawer).toBeVisible();
  await expect(page.locator('#tool-drawer-title')).toHaveText('cockpit.fixture.tool');
  await expect(page.locator('#tool-drawer-downloads')).toBeVisible();
  await expect(page.locator('#tool-drawer-verified')).toBeVisible();
  await expect(page.locator('#tool-drawer-update-cmd')).toContainText('dotnet tool update --local cockpit.fixture.tool');
  await expect(page.locator('#tool-drawer-uninstall-cmd')).toContainText('dotnet tool uninstall --local cockpit.fixture.tool');

  await page.locator('#tool-drawer-close').click();
  await expect(drawer).toBeHidden();
});
