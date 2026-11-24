import { expect, test } from '@playwright/test';
import { createPrivateRoom, submitRoomJoin } from './room-setup';

test('wild selection and card flight are visible to both connected players', async ({ page, browser }, testInfo) => {
  test.setTimeout(120_000);
  const context = await browser.newContext();
  try {
    const friend = await context.newPage();
    await page.setExtraHTTPHeaders({ 'X-Test-Deal': 'wild' });
    await createPrivateRoom(page, 'Visual Host', 2);
    const code = (await page.locator('.code-inline').textContent())!.trim();
    await submitRoomJoin(friend, 'Visual Friend', code);
    await expect(page.getByRole('heading', { name: /Players 2\/2/ })).toBeVisible();
    await page.getByRole('button', { name: /Start game/ }).click();
    await expect(page.locator('.table-felt')).toBeVisible();
    await expect(friend.locator('.table-felt')).toBeVisible();
    // Observe insertion rather than racing the short-lived animation.
    for (const client of [page, friend]) {
      await client.evaluate(() => {
        document.body.dataset.flightSeen = 'false';
        const observer = new MutationObserver(() => {
          if (document.querySelector('.play-flight')) {
            document.body.dataset.flightSeen = 'true';
            observer.disconnect();
          }
        });
        observer.observe(document.body, { childList: true, subtree: true });
      });
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('.hand-card').filter({ has: page.locator('b', { hasText: /^WILD$/ }) }).first().click();
    const picker = page.locator('.color-picker');
    await expect(picker).toBeVisible();
    for (const color of ['Red', 'Yellow', 'Green', 'Blue']) {
      await expect(picker.getByRole('button', { name: color, exact: true })).toBeEnabled();
    }
    await testInfo.attach('wild-picker-mobile', { body: await page.screenshot(), contentType: 'image/png' });
    await picker.getByRole('button', { name: 'Green', exact: true }).click();
    await expect(picker).toHaveCount(0);
    for (const client of [page, friend]) {
      await expect(client.locator('.active-color-chip')).toHaveText('Active color: Green');
      await expect(client.locator('.top-card')).toHaveClass(/selected-wild-color-green/);
      await expect(client.locator('body')).toHaveAttribute('data-flight-seen', 'true');
    }
    await expect(page.locator('.hand-card')).toHaveCount(6);
  } finally {
    await context.close();
  }
});

test('draw and discard controls do not overlap at supported portrait and desktop sizes', async ({ page, browser }) => {
  const context = await browser.newContext();
  try {
    const friend = await context.newPage();
    await createPrivateRoom(page, 'Layout Host', 2);
    const code = (await page.locator('.code-inline').textContent())!.trim();
    await submitRoomJoin(friend, 'Layout Friend', code);
    await expect(page.getByRole('heading', { name: /Players 2\/2/ })).toBeVisible();
    await page.getByRole('button', { name: /Start game/ }).click();
    await expect(page.locator('.table-felt')).toBeVisible();
    for (const viewport of [{ width: 360, height: 800 }, { width: 390, height: 844 }, { width: 1280, height: 720 }, { width: 1920, height: 1080 }]) {
      await page.setViewportSize(viewport);
      const draw = await page.locator('.draw-pile-button').boundingBox();
      const discard = await page.locator('.top-card').boundingBox();
      expect(draw).not.toBeNull();
      expect(discard).not.toBeNull();
      const overlapX = Math.min(draw!.x + draw!.width, discard!.x + discard!.width) - Math.max(draw!.x, discard!.x);
      const overlapY = Math.min(draw!.y + draw!.height, discard!.y + discard!.height) - Math.max(draw!.y, discard!.y);
      expect(overlapX <= 2 || overlapY <= 2, 'Draw and discard overlap at ' + viewport.width).toBeTruthy();
      await expect(page.locator('.draw-pile-button strong')).toBeVisible();
    }
  } finally {
    await context.close();
  }
});
