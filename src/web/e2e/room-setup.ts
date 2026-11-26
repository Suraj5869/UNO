import { expect, type Page } from '@playwright/test';

export async function openFriends(page: Page, name: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign In as Guest' }).click({ timeout: 10000 });
  await page.getByLabel('Your display name').fill(name, { timeout: 10000 });
  await page.getByRole('button', { name: /Play with Friends/ }).click({ timeout: 10000 });
  await expect(page.getByLabel('Room code')).toBeVisible({ timeout: 10000 });
}

export async function createPrivateRoom(page: Page, name: string, capacity: number, rule: string = '0') {
  await openFriends(page, name);
  await page.getByRole('button', { name: /Create private room/ }).click({ timeout: 10000 });
  await page.locator('input[name="create-rules"]').nth(Number(rule)).check({ timeout: 10000 });
  // Production UI always creates ten-seat rooms. Override only the test request
  // to exercise the backend's supported smaller capacities, retaining fixture headers.
  if (capacity !== 10) {
    await page.route('**/api/rooms', async route => {
      if (route.request().method() !== 'POST') return route.continue();
      await route.continue({ postData: JSON.stringify({ ...route.request().postDataJSON(), maximumPlayers: capacity }) });
    });
  }
  await page.getByRole('button', { name: /Create private room/ }).click({ timeout: 10000 });
  await expect(page.locator('.code-inline')).toHaveText(/^[A-Z0-9]{6}$/, { timeout: 10000 });
}

export async function submitRoomJoin(page: Page, name: string, code: string) {
  await openFriends(page, name);
  await page.getByLabel('Room code').fill(code, { timeout: 10000 });
  await page.getByRole('button', { name: /Join room/ }).click({ timeout: 10000 });
}
