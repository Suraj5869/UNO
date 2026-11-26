import { expect, test } from '@playwright/test';
import { createPrivateRoom, submitRoomJoin } from './room-setup';

const viewports = [
  { width: 360, height: 800 },
  { width: 390, height: 844 },
  { width: 768, height: 1024 },
  { width: 844, height: 390 },
  { width: 1280, height: 720 },
  { width: 1920, height: 1080 },
];

test('guest navigation and private room settings remain usable across screen sizes', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign In as Guest' }).click();
  await page.getByLabel('Your display name').fill('Responsive Host');
  for (const viewport of viewports) {
    await page.setViewportSize(viewport);
    await expect(page.getByRole('button', { name: /Play Match/ })).toBeVisible();
    await page.getByRole('button', { name: /Play with Friends/ }).click();
    await expect(page.getByLabel('Room code')).toBeVisible();
    await page.getByRole('button', { name: /Create private room/ }).click();
    await expect(page.locator('input[name="create-rules"]')).toHaveCount(2);
    await expect(page.getByRole('button', { name: /Create private room/ })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width + 1);
    await page.getByRole('button', { name: /Back to friends/ }).click();
    await page.getByRole('button', { name: /Back to home/ }).click();
  }
});

test('game table keeps controls, card counts and turn indicators accessible across screen sizes', async ({ page, browser }, testInfo) => {
  test.setTimeout(120_000);
  const context = await browser.newContext();
  try {
    const friend = await context.newPage();
    await createPrivateRoom(page, 'Responsive Host', 2);
    const code = (await page.locator('.code-inline').textContent())!.trim();
    await submitRoomJoin(friend, 'Responsive Friend', code);
    await expect(page.getByRole('heading', { name: /Players 2\/2/ })).toBeVisible();
    await page.getByRole('button', { name: /Start game/ }).click();
    await expect(page.locator('.table-felt')).toBeVisible();
    await expect(friend.locator('.table-felt')).toBeVisible();
    for (const viewport of viewports) {
      await page.setViewportSize(viewport);
      await expect(page.locator('.hand-card')).toHaveCount(7);
      await expect(page.locator('.opponent strong')).toHaveText('Responsive Friend');
      for (const selector of ['.draw-pile-button', '.draw-pile-button strong', '.top-card', '.active-color-chip', '.uno-call-button', '.hand-card:last-child']) {
        const element = page.locator(selector);
        await element.scrollIntoViewIfNeeded();
        await expect(element).toBeInViewport();
      }
      // Capture the real rendered table for review; no unapproved golden baseline.
      await testInfo.attach('table-' + viewport.width + 'x' + viewport.height, {
        body: await page.screenshot({ animations: 'disabled' }), contentType: 'image/png'
      });
      await expect(page.locator('.draw-pile-button strong')).toHaveText(/^\d+$/);
      await expect(page.locator('.active-color-chip')).toHaveText(/Active color: (Red|Blue|Green|Yellow)/i);
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width + 1);
    }
    await friend.getByRole('button', { name: 'Leave room' }).click();
    await expect(page.locator('.game-over-overlay')).toBeVisible();
    for (const viewport of viewports) {
      await page.setViewportSize(viewport);
      for (const name of [/Play Again/, /Back to Home/]) {
        const button = page.locator('.game-over-overlay').getByRole('button', { name });
        await button.scrollIntoViewIfNeeded();
        await expect(button).toBeInViewport();
        await expect(button).toBeEnabled();
      }
    }
  } finally {
    await context.close();
  }
});
