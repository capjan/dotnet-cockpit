import { expect, test } from '@playwright/test';

// Machine-specific text stays out of the comparison.
const masks = page => ['.checked', '.ws-card', '.working-directory', '.ws-head-copy', '.hero-meta-row', '.hero-stat-cell .muted', '.step code', '.settings-dl']
  .map(selector => page.locator(selector));

async function shoot(page, name) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
  await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: true, mask: masks(page), animations: 'disabled' });
}

const pages = ['mixed-overview', 'mixed-versions', 'mixed-tools', 'mixed-settings', 'mixed-preflight'];

for (const colorScheme of ['dark', 'light']) {
  test.describe(colorScheme, () => {
    test.use({ colorScheme, viewport: { width: 1440, height: 900 } });
    for (const name of pages) {
      test(name, async ({ page }) => {
        await page.goto(`/${name}`);
        await shoot(page, `${name}-${colorScheme}`);
      });
    }
  });
}

test.describe('states', () => {
  test.use({ colorScheme: 'dark', viewport: { width: 1440, height: 900 } });
  for (const name of ['healthy-overview', 'healthy-versions', 'empty-overview', 'empty-versions', 'empty-tools']) {
    test(name, async ({ page }) => {
      await page.goto(`/${name}`);
      await shoot(page, name);
    });
  }

  test('version drawer', async ({ page }) => {
    await page.goto('/mixed-versions');
    await page.locator('[data-inspect="9.0.320"]').click();
    await shoot(page, 'overlay-version-drawer');
  });

  test('removal confirmation', async ({ page }) => {
    await page.goto('/mixed-versions');
    await page.locator('tr[data-drawer-key="7.0.408"] button.remove').click();
    await shoot(page, 'overlay-confirm');
  });

  test('clean up', async ({ page }) => {
    await page.goto('/mixed-versions');
    await page.locator('#open-cleanup-btn').click();
    await shoot(page, 'overlay-cleanup');
  });
});

for (const palette of ['macos', 'github', 'codex']) {
  for (const colorScheme of ['dark', 'light']) {
    test.describe(`${palette} ${colorScheme}`, () => {
      test.use({ colorScheme, viewport: { width: 1440, height: 900 } });
      for (const name of ['mixed-versions', 'mixed-overview']) {
        test(name, async ({ page }) => {
          await page.goto(`/${name}`);
          await page.evaluate(value => { document.documentElement.dataset.palette = value; }, palette);
          await shoot(page, `${palette}-${name}-${colorScheme}`);
        });
      }
    });
  }
}

test.describe('narrow desktop', () => {
  test.use({ colorScheme: 'dark', viewport: { width: 1024, height: 768 } });
  for (const name of ['mixed-versions', 'mixed-tools', 'mixed-overview']) {
    test(name, async ({ page }) => {
      await page.goto(`/${name}`);
      await shoot(page, `${name}-1024`);
    });
  }
});
