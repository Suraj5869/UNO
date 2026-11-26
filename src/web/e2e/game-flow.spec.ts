import { createPrivateRoom, submitRoomJoin } from './room-setup';
import { expect, test } from '@playwright/test';

test('host creates a room, friend joins, and host starts a match', async ({ browser, page }) => {
  await createPrivateRoom(page, 'Host', Number('2'));

  await expect(page.getByRole('heading', { name: /Room/ })).toBeVisible();
  const roomCode = (await page.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  const friendContext = await browser.newContext();
  const friendPage = await friendContext.newPage();
  await submitRoomJoin(friendPage, 'Friend', roomCode!);

  await expect(friendPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Players 2\/2/ })).toBeVisible();

  await page.getByRole('button', { name: /Start game/ }).click();
  await expect(page.locator('.table-felt')).toBeVisible();
  await expect(page.locator('.hand-card')).toHaveCount(7);
  await expect(friendPage.locator('.table-felt')).toBeVisible();

  await friendContext.close();
});

test('joining an unknown room shows a useful error', async ({ page }) => {
  await submitRoomJoin(page, 'Visitor', 'ZZZZZZ');
  await expect(page.getByText(/Room not found|not found/i)).toBeVisible();
});
