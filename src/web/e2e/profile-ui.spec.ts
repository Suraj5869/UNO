import { expect, test } from '@playwright/test';

const player = { userId: 'ui-user', publicPlayerId: 'ui-player', email: 'ui@example.com', username: 'uitester', displayName: 'Tester', avatarKey: '', level: 1, totalXp: 0, matchesPlayed: 0, wins: 0 };
const items = [
  { itemKey: 'avatar-default', category: 'avatar', displayName: 'Default avatar', owned: true, equipped: true },
  { itemKey: 'back-default', category: 'card_back', displayName: 'Classic back', owned: true, equipped: true },
  { itemKey: 'table-default', category: 'table_theme', displayName: 'Classic table', owned: true, equipped: true },
];

test('profile popup, collection retry, tabs and name updates remain consistent', async ({ page }, testInfo) => {
  await page.route('**/api/auth/refresh', route => route.fulfill({ json: { accessToken: 'ui-token', expiresIn: 3600, player } }));
  await page.route('**/api/progression', route => route.fulfill({ json: { level: 1, totalXp: 0, nextLevelXp: 100, season: 'Test', seasonalXp: 0, seasonalLevel: 1, seasonEndsAt: '2030-01-01', matchesPlayed: 0, wins: 0, losses: 0, winRate: 0, modeStatistics: [] } }));
  let current = { ...player };
  await page.route('**/api/auth/me', async route => {
    if (route.request().method() === 'PUT') current = { ...current, ...route.request().postDataJSON() };
    await route.fulfill({ json: current });
  });
  let collectionAvailable = false;
  await page.route('**/api/auth/collection', async route => {
    if (!collectionAvailable) await route.fulfill({ status: 404 });
    else await route.fulfill({ json: items });
  });
  await page.goto('/');
  const trigger = page.getByRole('button', { name: 'Player profile' });
  await trigger.click();
  await expect(page.locator('.profile-popup-heading .profile-circle')).toHaveText('T');
  await page.keyboard.press('Escape');
  await expect(trigger).toBeFocused();
  await trigger.click();
  await page.getByRole('button', { name: 'Profile & collections' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.collection-portrait')).toHaveText('T');
  await dialog.getByRole('button', { name: 'Collections', exact: true }).click();
  await expect(dialog.getByRole('alert')).toContainText('Collection API not found');
  collectionAvailable = true;
  await dialog.getByRole('button', { name: 'Retry' }).click();
  await expect(dialog.getByText('Default avatar')).toBeVisible();
  for (const viewport of [{ width: 360, height: 800 }, { width: 390, height: 844 }, { width: 1280, height: 720 }]) {
    await page.setViewportSize(viewport);
    for (const [tab, item] of [['Card backs', 'Classic back'], ['Table themes', 'Classic table'], ['Avatars', 'Default avatar']]) {
      await dialog.getByRole('button', { name: tab, exact: true }).click();
      await expect(dialog.getByText(item, { exact: true })).toBeVisible();
    }
    await expect(dialog.getByRole('button', { name: 'Close profile' })).toBeInViewport();
    await testInfo.attach('profile-' + viewport.width, { body: await page.screenshot({ animations: 'disabled' }), contentType: 'image/png' });
  }
  await dialog.getByLabel('Display name').fill('Updated Tester');
  await dialog.getByRole('button', { name: 'Save name' }).click();
  await expect(dialog.getByText('Display name updated.')).toBeVisible();
  await expect(dialog.locator('.collection-portrait')).toHaveText('U');
  await dialog.getByRole('button', { name: 'Close profile' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(trigger.locator('.profile-circle')).toHaveText('U');
  await trigger.click();
  await expect(page.locator('.profile-popup-heading .profile-circle')).toHaveText('U');
});
