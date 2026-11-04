import { expect, test } from '@playwright/test';

test('receives the real inventory in the initial HTML with version-specific directories', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  const runtimeCards = new Set(snapshot.runtimes.map(runtime => runtime.version)).size;
  const response = await page.goto('/');
  const html = await response.text();
  expect(html).toContain('<tr data-kind="sdk"');
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
    await page.locator('.sort-menu summary').click();
    await page.locator('.sort-menu').getByRole('link', { name: 'Oldest version' }).click();
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
  const theme = page.locator('header [data-theme-pref="dark"]');
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
  await page.getByRole('link', { name: /^Refresh/ }).click();
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
  await expect(page.locator('tbody.channel-card:not([hidden])')).toHaveCount(1);

  await searchBox.fill('xyz-nonexistent-query');
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(0);
  await expect(page.locator('tbody.channel-card:not([hidden])')).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'No matching versions' })).toBeVisible();

  await searchBox.fill('');
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(totalRows);

  // Toggling SDKs, Runtimes, and Previews filter pills does not scroll the page
  const nav = page.getByRole('navigation', { name: 'Installation type' });
  await nav.getByRole('link', { name: 'SDKs', exact: true }).click();
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(snapshot.sdks.length);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  await nav.getByRole('link', { name: 'SDKs', exact: true }).click();
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(totalRows);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  await nav.getByRole('link', { name: 'Runtimes', exact: true }).click();
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(runtimeCards);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  await nav.getByRole('link', { name: 'Previews', exact: true }).click();
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  await nav.getByRole('link', { name: 'Previews', exact: true }).click();
  await expect(page.locator('tbody tr[data-kind]:not([hidden])')).toHaveCount(totalRows);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
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
  await page.getByRole('button', { name: /Open cockpit/i }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: 'Versions', level: 1 })).toBeVisible();
});

test('opens detail drawer when inspecting a version and displays equivalent command', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  await page.goto('/');

  const firstSdk = snapshot.sdks[0];
  await expect(page.locator('#version-drawer')).toBeHidden();

  // Click on the row for firstSdk to inspect it
  await page.locator(`tbody tr[data-kind="sdk"]`).filter({ hasText: firstSdk.version }).first().click();
  await expect(page.locator('#version-drawer')).toBeVisible();
  await expect(page.locator('#drawer-title')).toHaveText(firstSdk.version);
  if (await page.locator('#drawer-command').count()) {
    await expect(page.locator('#drawer-command')).toContainText(`dotnet-core-uninstall remove ${firstSdk.version} \\`);
    await expect(page.locator('#drawer-command')).toContainText('--sdk \\');
  }
  await expect(page.locator('#version-drawer .drawer-patch-box')).toBeVisible();
  await expect(page.locator('#version-drawer .drawer-primary-btn')).toBeVisible();

  // Close drawer
  await page.locator('#drawer-close').click();
  await expect(page.locator('#version-drawer')).toBeHidden();
});

test('opens the Install .NET side-drawer with Latest filter active and filters SDK and runtime releases', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.rail a[href="/preflight"]')).toHaveCount(0);
  const channelCards = await page.locator('tbody.channel-card').count();
  const removeChannelBtns = await page.locator('[data-remove-channel]').count();
  // Removal needs dotnet-core-uninstall, which not every machine has. Where it exists, every channel offers it.
  expect([0, channelCards]).toContain(removeChannelBtns);

  const openBtn = page.locator('#open-install-dotnet-btn');
  await expect(openBtn).toBeVisible();
  await openBtn.click();

  const dialog = page.locator('#install-dotnet-dialog');
  await expect(dialog).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Install .NET' })).toBeVisible();
  await expect(page.locator('[data-install-lifecycle="latest"]')).toHaveClass(/active/);
  await expect(page.locator('[data-install-lifecycle="lts"]')).toHaveText('LTS');
  const firstCommand = dialog.locator('[data-install-row]:not([hidden]) .row-command').first();
  await firstCommand.locator('summary').click();
  await expect(firstCommand).toContainText('sudo installer -pkg');
  await firstCommand.locator('summary').click();
  const firstChannel = dialog.locator('[data-install-channel-group]:not([hidden])').first();
  await firstChannel.locator('.sheet-channel-head').click();
  await expect(firstChannel.locator('[data-install-row]:not([hidden])').first()).toBeHidden();
  await firstChannel.locator('.sheet-channel-head').click();

  const visibleGroups = page.locator('[data-install-channel-group]:not([hidden])');
  expect(await visibleGroups.count()).toBeGreaterThan(0);

  const latestCount = await page.locator('[data-install-row]:not([hidden])').count();
  expect(latestCount).toBeGreaterThan(0);

  // Exactly one view is active at a time, and more patches means at least as many rows
  await page.locator('[data-install-lifecycle="supported"]').click();
  await expect(page.locator('[data-install-lifecycle="supported"]')).toHaveClass(/active/);
  await expect(page.locator('[data-install-lifecycle="latest"]')).not.toHaveClass(/active/);
  expect(await page.locator('[data-install-row]:not([hidden])').count()).toBeGreaterThanOrEqual(latestCount);
  await page.locator('[data-install-lifecycle="latest"]').click();
  await expect(page.locator('[data-install-row]:not([hidden])')).toHaveCount(latestCount);

  await page.locator('[data-install-product=".NET Runtime"]').click();
  await expect(page.locator('[data-install-row]:not([hidden])').first()).toContainText('.NET Runtime');

  await page.locator('#close-install-dotnet-btn').click();
  await expect(dialog).toBeHidden();

  // Open channel removal dialog and verify multi-line command & strategy pills
  const removeDialog = page.locator('#remove-dialog');
  if (removeChannelBtns > 0) {
    await page.locator('[data-remove-channel]').first().click();
    await expect(removeDialog).toBeVisible();
    await expect(page.locator('#remove-dialog-cmd')).toContainText('dotnet-core-uninstall remove');
    await page.locator('#remove-dialog-cancel').click();
    await expect(removeDialog).toBeHidden();
  }

  // Smart cleanup: a single "Clean up…" action opens the removal dialog with strategy cards
  const cleanupBtn = page.locator('#open-cleanup-btn');
  if (await cleanupBtn.count() > 0) {
    await cleanupBtn.click();
    await expect(removeDialog).toBeVisible();
    await expect(page.locator('#remove-dialog-title')).toHaveText('Clean up .NET installations');
    await expect(page.locator('#remove-dialog-modes')).toBeVisible();
    await expect(page.locator('.cleanup-strategy-card:not([hidden])').first()).toBeVisible();
    await expect(page.locator('#remove-dialog-items input:checked')).toHaveCount(await page.locator('#remove-dialog-items input').count());
    await page.locator('#remove-dialog-cancel').click();
    await expect(removeDialog).toBeHidden();
  }
  await expect(page.locator('[data-update-channel]')).toHaveCount(0);
});

test('switches between auto, light and dark appearance', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  const sw = page.locator('header [data-theme-switch]');
  await expect(sw).toBeVisible();
  await sw.locator('[data-theme-pref="light"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await sw.locator('[data-theme-pref="dark"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await sw.locator('[data-theme-pref="auto"]').click();
  await expect(sw.locator('[data-theme-pref="auto"]')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'auto');
});

test('renders the overview page with system summary and health cards', async ({ page }) => {
  await page.goto('/overview');
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.getByText('Installed SDKs', { exact: true })).toBeVisible();
  await expect(page.getByText('System Health')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'This workspace' })).toBeVisible();
  await expect(page.getByText('Support Lifecycle Timeline')).toBeVisible();
  await expect(page.locator('.tl-today-pill')).toHaveText('Today');
});

test('the command palette finds tools and versions from any page and says when nothing matches', async ({ page, request }) => {
  const snapshot = await (await request.get('/api/installation')).json();
  await page.goto('/overview');
  await page.keyboard.press('Meta+k');
  const input = page.locator('#cmdk-input');
  await expect(input).toBeFocused();
  await input.fill(snapshot.sdks[0].version);
  await expect(page.locator('#cmdk-dialog .opt:not([hidden])').first()).toContainText(snapshot.sdks[0].version);
  await input.fill('zzzz-no-such-thing');
  await expect(page.locator('#cmdk-empty')).toBeVisible();
  await expect(page.locator('#cmdk-nuget')).toHaveAttribute('href', /q=zzzz-no-such-thing/);
});

test('remembers the appearance across pages through the settings file', async ({ page }) => {
  await page.goto('/');
  await page.locator('header [data-theme-pref="light"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.waitForLoadState('networkidle');
  await page.goto('/overview');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.locator('header [data-theme-pref="auto"]').click();
  await page.waitForLoadState('networkidle');
});

test('all row actions line up with the channel action in every channel', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const edges = await page.evaluate(() => [...document.querySelectorAll('[data-remove-channel], tr[data-kind] .remove')]
    .map(element => { const box = element.getBoundingClientRect(); return [Math.round(box.right * 10) / 10, Math.round(box.width * 10) / 10]; }));
  if (edges.length) {
    expect(new Set(edges.map(edge => edge[0])).size).toBe(1);
    expect(new Set(edges.map(edge => edge[1])).size).toBe(1);
  }
});

test('filters keep their position and height while switching', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const measure = () => page.evaluate(() => [...document.querySelectorAll('[data-version-kind], .inventory .filter-form .search-field')]
    .map(element => { const box = element.getBoundingClientRect(); return { left: Math.round(box.left), top: Math.round(box.top), height: Math.round(box.height) }; }));
  const before = await measure();
  await page.locator('[data-version-kind="sdks"]').click();
  const after = await measure();
  expect(after.map(item => item.left)).toEqual(before.map(item => item.left));
  expect(new Set(before.filter((_, index) => index > 2).map(item => item.height)).size).toBe(1);
});

test('without a dotnet host the overview says so instead of reporting zero tools', async ({ page }) => {
  await page.goto('http://127.0.0.1:5232/overview');
  await expect(page.locator('.hero-meta-row')).not.toContainText('dotnet on PATH');
  await expect(page.locator('.hero-stat-cell').nth(2)).toContainText('Not available');
});

test('every control works without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  try {
    for (const path of ['/', '/tools', '/tools?scope=local', '/overview', '/setup', '/preflight', '/?install=1']) {
      await page.goto(path);
      await expect(page.locator('button[type="button"]:visible'), path).toHaveCount(0);
    }
    await page.goto('/?install=1');
    await page.locator('[data-install-product=".NET Runtime"]').click();
    await expect(page).toHaveURL(/product=runtime/);
    await expect(page.locator('#install-dotnet-dialog')).toBeVisible();
    await expect(page.locator('[data-install-row]:not([hidden])').first()).toContainText('.NET Runtime');
    await page.locator('[data-install-lifecycle="all"]').click();
    await expect(page).toHaveURL(/view=all/);
    await page.goto('/');
    await page.locator('header [data-theme-pref="dark"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.locator('header [data-theme-pref="auto"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'auto');
  } finally { await context.close(); }
});

test('no table scrolls sideways at desktop widths', async ({ page }) => {
  for (const width of [1920, 1440, 1280, 1100, 1024]) {
    await page.setViewportSize({ width, height: 800 });
    for (const path of ['/', '/overview', '/setup', 'http://127.0.0.1:5234/tools', 'http://127.0.0.1:5234/tools?scope=all']) {
      await page.goto(path);
      const overflow = await page.evaluate(() => ({
        page: document.documentElement.scrollWidth - innerWidth,
        table: Math.max(0, ...[...document.querySelectorAll('.table-scroll')].map(element => element.scrollWidth - element.clientWidth)),
      }));
      expect(overflow, `${path} at ${width}`).toEqual({ page: 0, table: 0 });
    }
  }
});

test('overlays return focus and keep the keyboard inside', async ({ page }) => {
  await page.goto('/');
  const inspect = page.locator('[data-inspect]').first();
  await inspect.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#drawer-close')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(inspect).toBeFocused();

  const install = page.locator('#open-install-dotnet-btn');
  await install.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#install-dotnet-search-input')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(install).toBeFocused();

  await page.keyboard.press('Meta+k');
  await expect(page.locator('#cmdk-input')).toBeFocused();
  for (let index = 0; index < 4; index++) await page.keyboard.press('Tab');
  expect(await page.evaluate(() => Boolean(document.activeElement.closest('#cmdk-dialog')))).toBe(true);
  await page.keyboard.press('Escape');
  await expect(page.locator('#cmdk-dialog')).toBeHidden();
});

test('the page notices a changed installation when the window regains focus, without polling', async ({ page }) => {
  let calls = 0;
  page.on('request', request => { if (request.url().endsWith('/api/installation')) calls++; });
  await page.goto('/');
  await page.waitForTimeout(500);
  expect(calls).toBe(0);
  await page.route('**/api/installation', async route => {
    const response = await route.fetch();
    const json = await response.json();
    await route.fulfill({ response, json: { ...json, signature: 'changed' } });
  });
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(page.locator('.toast')).toContainText('versions changed');
});

test('digits in the navigation share one vertical line', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const centers = await page.evaluate(() => [...document.querySelectorAll('.nav-link .nav-count, .nav-link .nav-badge')]
    .map(element => { const box = element.getBoundingClientRect(); return Math.round(box.left + box.width / 2); }));
  expect(centers.length).toBeGreaterThan(1);
  expect(new Set(centers).size).toBe(1);
});

test('the navigation stays compact when the window is narrow and the page is short', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1100 });
  await page.goto('/setup');
  const height = await page.evaluate(() => document.querySelector('.rail').getBoundingClientRect().height);
  expect(height).toBeLessThan(80);
});
