import { expect, test } from '@playwright/test';

test('private room forms are nested under Play with Friends', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: /Play Match/ })).toBeVisible();
  await expect(page.getByLabel('Maximum players')).toHaveCount(0);
  await page.getByRole('button', { name: /Play with Friends/ }).click();
  await expect(page.getByLabel('Maximum players')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Join room' })).toBeVisible();
  await page.getByRole('button', { name: /Back to home/ }).click();
  await expect(page.getByRole('button', { name: /Play Match/ })).toBeVisible();
});

test('two random players enter the same automatically started game', async ({ page, browser }) => {
  const other = await browser.newContext();
  try {
    const opponent = await other.newPage();
    await page.goto('/');
    await page.getByLabel('Your display name').fill('Quick One');
    await page.getByRole('button', { name: /Play Match/ }).click();
    await expect(page.getByText('Finding a match…')).toBeVisible();
    await opponent.goto('/');
    await opponent.getByLabel('Your display name').fill('Quick Two');
    await opponent.getByRole('button', { name: /Play Match/ }).click();
    for (const player of [page, opponent]) {
      await expect(player.getByText('MATCH IN PROGRESS')).toBeVisible();
      await expect(player.locator('.hand-card')).toHaveCount(7);
    }
    expect(await page.locator('.code-inline').textContent()).toBe(await opponent.locator('.code-inline').textContent());
  } finally { await other.close(); }
});
