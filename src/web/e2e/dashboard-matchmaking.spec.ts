import { expect, test, type Page } from '@playwright/test';

async function openGuest(page: Page, name: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign In as Guest' }).click();
  await page.getByLabel('Your display name').fill(name);
}

test('private room forms are nested under Play with Friends', async ({ page }) => {
  await openGuest(page, 'Private Host');
  await expect(page.getByRole('button', { name: /Ranked/ })).toBeVisible();
  await expect(page.getByLabel('Room code')).toHaveCount(0);
  await expect(page.locator('input[name="create-rules"]')).toHaveCount(0);
  await page.getByRole('button', { name: /Play with Friends/ }).click();
  await expect(page.getByLabel('Room code')).toBeVisible();
  await expect(page.getByRole('button', { name: /Join room/ })).toBeVisible();
  await page.getByRole('button', { name: /Create private room/ }).click();
  await expect(page.getByRole('heading', { name: 'Create Room' })).toBeVisible();
  await expect(page.locator('input[name="create-rules"]')).toHaveCount(2);
  await page.getByRole('button', { name: /Back to friends/ }).click();
  await expect(page.getByLabel('Room code')).toBeVisible();
  await page.getByRole('button', { name: /Back to home/ }).click();
  await expect(page.getByRole('button', { name: /Ranked/ })).toBeVisible();
});

test('two random players enter the same automatically started game', async ({ page, browser }) => {
  const other = await browser.newContext();
  try {
    const opponent = await other.newPage();
    await openGuest(page, 'Quick One');
    await page.getByRole('button', { name: /Ranked/ }).click();
    await page.getByRole('button', { name: /Find match/ }).click();
    await expect(page.getByText('Finding a match…')).toBeVisible();
    await openGuest(opponent, 'Quick Two');
    await opponent.getByRole('button', { name: /Ranked/ }).click();
    await opponent.getByRole('button', { name: /Find match/ }).click();
    for (const player of [page, opponent]) {
      await expect(player.locator('.table-felt')).toBeVisible();
      await expect(player.locator('.hand-card')).toHaveCount(7);
      await expect(player.locator('.code-inline')).toHaveText(/^[A-Z0-9]{6}$/);
    }
    expect(await page.locator('.code-inline').textContent()).toBe(await opponent.locator('.code-inline').textContent());
    await expect(page.locator('.opponent strong')).toHaveText('Quick Two');
    await expect(opponent.locator('.opponent strong')).toHaveText('Quick One');
  } finally { await other.close(); }
});
