import { expect, test } from '@playwright/test';

test('host creates a room, friend joins, and host starts a match', async ({ browser, page }) => {
  await page.goto('/');
  await page.getByLabel('Your display name').fill('Host');
  await page.getByLabel('Maximum players').selectOption('2');
  await page.getByRole('button', { name: /Create private room/ }).click();

  await expect(page.getByRole('heading', { name: /Room/ })).toBeVisible();
  const roomCode = (await page.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  const friendContext = await browser.newContext();
  const friendPage = await friendContext.newPage();
  await friendPage.goto('/');
  await friendPage.getByLabel('Your display name').fill('Friend');
  await friendPage.getByLabel('Room code').fill(roomCode!);
  await friendPage.getByRole('button', { name: 'Join room' }).click();

  await expect(friendPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Players 2\/2/ })).toBeVisible();

  await page.getByRole('button', { name: /Start game/ }).click();
  await expect(page.getByText('MATCH IN PROGRESS')).toBeVisible();
  await expect(page.locator('.hand-card')).toHaveCount(7);
  await expect(friendPage.getByText('MATCH IN PROGRESS')).toBeVisible();

  await friendContext.close();
});

test('joining an unknown room shows a useful error', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Your display name').fill('Visitor');
  await page.getByLabel('Room code').fill('ZZZZZZ');
  await page.getByRole('button', { name: 'Join room' }).click();
  await expect(page.getByText(/Room not found|not found/i)).toBeVisible();
});
