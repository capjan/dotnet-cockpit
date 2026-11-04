// Starts the real native binary on every operating system and uses each feature the way a person would.
// Tools really get installed, into a temporary CLI home. Only a Windows CI runner installs and removes .NET itself.
import { expect, test } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const TOOL = 'mdu';
const ACTION_TIMEOUT = 5 * 60_000;

test.describe.configure({ mode: 'serial' });

function dotnet(...args) {
  const result = spawnSync('dotnet', args, { encoding: 'utf8', shell: process.platform === 'win32' });
  expect(result.status, `dotnet ${args.join(' ')}\n${result.stderr}`).toBe(0);
  return result.stdout;
}

/** Confirms the shared dialog, checks what it says it will run, and waits for the success message. */
async function confirmAndWait(page, command, success) {
  const dialog = page.locator('#confirm-dialog');
  await expect(dialog).toBeVisible();
  if (command) await expect(page.locator('#confirm-dialog-command')).toContainText(command);
  await page.locator('#confirm-dialog-submit').click();
  await expect(page.locator('.toast')).toContainText(success, { timeout: ACTION_TIMEOUT });
}

async function searchAndInstall(page, scope) {
  await page.locator('#open-add-tool-btn').click();
  const sheet = page.locator('#add-tool-dialog');
  await expect(sheet).toBeVisible();
  if (scope === 'local') await sheet.locator('[data-sheet-scope="local"]').click();
  await page.locator('#sheet-search-input').fill(TOOL);
  const row = sheet.locator(`[data-pkg-id="${TOOL}"]`);
  await expect(row).toBeVisible({ timeout: 30_000 });
  await row.locator('button[type="submit"]').click();
  await confirmAndWait(page, `dotnet tool install --${scope}`, 'Tool installed.');
}

test('every page opens and works without errors', async ({ page }) => {
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/overview');
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.locator('.hero-headline')).not.toBeEmpty();

  await page.locator('a.nav-link[href="/"]').click();
  await expect(page.getByRole('heading', { name: 'Versions', level: 1 })).toBeVisible();
  const rows = page.locator('tr[data-kind]');
  await expect(rows.first()).toBeVisible();
  const total = await rows.count();

  await page.locator('[data-version-kind="sdks"]').click();
  await expect(page.locator('tr[data-kind="runtime"]:not([hidden])')).toHaveCount(0);
  await page.locator('[data-version-kind="sdks"]').click();
  await expect(page.locator('tr[data-kind]:not([hidden])')).toHaveCount(total);

  await rows.first().locator('[data-inspect]').click();
  await expect(page.locator('#version-drawer')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.locator('#version-drawer')).toBeHidden();

  await page.locator('a.nav-link[href="/tools"]').click();
  await expect(page.getByRole('heading', { name: '.NET Tools', level: 1 })).toBeVisible();
  await page.locator('a.nav-link[href="/setup"]').click();
  await expect(page.getByRole('heading', { name: 'Settings', level: 1 })).toBeVisible();

  await page.locator('[data-palette-pref="github"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'github');
  await page.locator('header [data-theme-pref="light"]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.waitForLoadState('networkidle');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-palette', 'github');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

  await page.goto('/preflight');
  await expect(page.getByRole('heading', { name: 'Pre-flight check' })).toBeVisible();
  expect(errors).toEqual([]);
});

test('Install .NET offers what fits this operating system', async ({ page }) => {
  await page.goto('/?install=1');
  const sheet = page.locator('#install-dotnet-dialog');
  await expect(sheet).toBeVisible();
  const html = await page.content();
  if (process.platform === 'linux') {
    await expect(page.locator('#linux-install-guide')).toBeVisible();
    await expect(sheet.locator('[data-install-row]')).toHaveCount(0);
    expect(html).not.toContain('.pkg');
    expect(html).not.toContain('sudo installer');
    return;
  }
  const extension = process.platform === 'win32' ? '.exe' : '.pkg';
  const first = sheet.locator('[data-install-row]:not([hidden])').first();
  await expect(first).toBeVisible();
  await expect(first.locator('a[download]')).toHaveAttribute('href', new RegExp(`\\${extension}$`));
  expect(html).not.toContain(process.platform === 'win32' ? 'sudo installer' : '/install /quiet');
});

test('installs, inspects and removes a global tool', async ({ page }) => {
  await page.goto('/tools');
  await searchAndInstall(page, 'global');
  const row = page.locator(`tr[data-drawer-key="global:${TOOL}"]`);
  await expect(row).toBeVisible();
  expect(dotnet('tool', 'list', '--global')).toContain(TOOL);

  await row.locator('a[data-inspect]').click();
  await expect(page.locator('#tool-drawer-title')).toHaveText(TOOL);
  await page.keyboard.press('Escape');

  await row.getByRole('button', { name: `Remove ${TOOL}` }).click();
  await confirmAndWait(page, `dotnet tool uninstall --global ${TOOL}`, 'Tool removed.');
  await expect(row).toHaveCount(0);
  expect(dotnet('tool', 'list', '--global')).not.toContain(`${TOOL} `);
});

test('initializes a workspace, installs a local tool and removes both', async ({ page }) => {
  const manifest = join(process.env.COCKPIT_E2E_WORKSPACE, '.config', 'dotnet-tools.json');
  await page.goto('/tools?scope=local');
  await page.getByRole('button', { name: 'Initialize local tools' }).click();
  await confirmAndWait(page, 'dotnet new tool-manifest', 'Local tools are initialized');
  expect(existsSync(manifest)).toBe(true);

  await searchAndInstall(page, 'local');
  await page.goto('/tools?scope=local');
  const row = page.locator(`tr[data-drawer-key="local:${TOOL}"]`);
  await expect(row).toBeVisible();

  await row.getByRole('button', { name: `Remove ${TOOL}` }).click();
  await confirmAndWait(page, `dotnet tool uninstall --local ${TOOL}`, 'Tool removed.');
  await page.goto('/tools?scope=local');
  await expect(row).toHaveCount(0);

  await page.getByRole('button', { name: 'Remove local tools' }).click();
  await confirmAndWait(page, null, 'Local manifest removed');
  expect(existsSync(manifest)).toBe(false);
});

test('Linux explains that removing .NET is not available', async ({ page }) => {
  test.skip(process.platform !== 'linux', 'Only Linux has no uninstall tool.');
  await page.goto('/');
  await expect(page.locator('tr[data-kind]').first()).toBeVisible();
  await expect(page.locator('tr[data-kind] button.remove')).toHaveCount(0);
  await expect(page.locator('[data-remove-channel]')).toHaveCount(0);
  await page.goto('/setup');
  await expect(page.getByRole('heading', { name: 'This platform is not supported' })).toBeVisible();
});

test('Windows really installs and removes a .NET runtime', async ({ page }) => {
  test.skip(process.platform !== 'win32' || !process.env.CI, 'This changes the machine, so it only runs on a Windows CI runner.');

  // The uninstall tool itself, through the Settings page
  await page.goto('/setup');
  const install = page.locator('form[data-confirm-post="/setup/install"] button[type="submit"]');
  if (await install.count()) {
    await install.click();
    await confirmAndWait(page, null, 'was installed');
  }

  // An older runtime that is not installed yet, the way the Install .NET sheet offers it
  await page.goto('/?install=1&product=runtime&view=supported');
  const candidates = page.locator('[data-install-row][data-product=".NET Runtime"]:not([hidden])');
  await expect(candidates.first()).toBeVisible();
  const choice = await candidates.evaluateAll(rows => {
    const free = rows.filter(row => !row.querySelector('.v-ok') && row.dataset.preview !== 'true');
    const row = free.find(candidate => candidate.dataset.channel === '9') ?? free[0];
    return { version: row.dataset.version, url: row.querySelector('a[download]').href, sha: row.querySelector('.drawer-sha').dataset.copy };
  });
  expect(choice.url).toMatch(/\.exe$/);

  const response = await fetch(choice.url);
  expect(response.ok).toBe(true);
  const bytes = Buffer.from(await response.arrayBuffer());
  expect(createHash('sha512').update(bytes).digest('hex').toLowerCase()).toBe(choice.sha.toLowerCase());
  const installer = join(process.env.COCKPIT_E2E_DIR, 'runtime-installer.exe');
  writeFileSync(installer, bytes);
  const run = spawnSync(installer, ['/install', '/quiet', '/norestart'], { stdio: 'inherit' });
  expect([0, 3010]).toContain(run.status);
  expect(dotnet('--list-runtimes')).toContain(`Microsoft.NETCore.App ${choice.version}`);

  // Cockpit notices it, and removes it again
  await page.goto('/?refresh=1');
  const row = page.locator(`tr[data-drawer-key="${choice.version}"]`);
  await expect(row).toBeVisible();
  await row.locator('button.remove').click();
  await confirmAndWait(page, `dotnet-core-uninstall remove ${choice.version}`, '.NET installation was removed');
  await expect(row).toHaveCount(0);
  expect(dotnet('--list-runtimes')).not.toContain(`Microsoft.NETCore.App ${choice.version}`);
});
