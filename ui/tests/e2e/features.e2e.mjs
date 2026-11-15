// Starts the real native binary on every operating system and uses each feature the way a person would.
// Tools really get installed, into a temporary CLI home. .NET itself is never installed or removed, because macOS asks for an administrator password.
import { expect, test } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

const TOOL = 'mdu';
const ACTION_TIMEOUT = 5 * 60_000;

test.describe.configure({ mode: 'serial' });

function dotnet(...args) {
  const result = spawnSync('dotnet', args, { encoding: 'utf8' });
  expect(result.status, `dotnet ${args.join(' ')}\n${result.stderr}`).toBe(0);
  return result.stdout;
}

/** Confirms the shared dialog, checks what it says it will run, and waits for the success message. */
async function confirmAndWait(page, command, success) {
  const dialog = page.locator('#confirm-dialog');
  await expect(dialog).toBeVisible();
  if (command) await expect(page.locator('#confirm-dialog-command')).toContainText(command);
  await page.locator('#confirm-dialog-submit').click();
  // A failure shows a notice instead of a message, which should end the wait straight away.
  const outcome = page.locator('.toast, .action-notice.error-state');
  await expect(outcome).toBeVisible({ timeout: ACTION_TIMEOUT });
  await expect(outcome).toContainText(success);
  // A newly installed tool's details arrive just after the page, and the page then reloads itself once to show them.
  await page.waitForLoadState('networkidle');
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

test('Install .NET offers macOS packages', async ({ page }) => {
  await page.goto('/?install=1');
  const sheet = page.locator('#install-dotnet-dialog');
  await expect(sheet).toBeVisible();
  const first = sheet.locator('[data-install-row]:not([hidden])').first();
  await expect(first).toBeVisible();
  await expect(first.locator('a[download]')).toHaveAttribute('href', /\.pkg$/);
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
