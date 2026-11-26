import { test, expect } from '@playwright/test';

const player = { userId: 'test-user', publicPlayerId: 'test-player', email: 'test@example.com', username: 'tester', displayName: 'Tester', avatarKey: '', level: 1, totalXp: 0, matchesPlayed: 0, wins: 0 };
const session = { accessToken: 'old-token', expiresIn: 3600, player };

test('cookie restoration keeps account credentials out of browser storage', async ({ page }) => {
  let refreshes = 0;
  await page.route('**/api/auth/refresh', route => {
    refreshes++;
    expect(route.request().headers()['x-uno-browser']).toBe('1');
    expect(route.request().postDataJSON()).toEqual({});
    return route.fulfill({ json: session });
  });
  await page.route('**/api/auth/me', route => route.fulfill({ status: 503 }));
  await page.goto('/');
  await expect.poll(() => refreshes).toBe(1);
  await expect(page.getByRole('heading', { name: 'Sign In', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => sessionStorage.getItem('uno-account-session'))).toBeNull();
});

test('invalid refresh token returns to sign in', async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('uno-account-session', JSON.stringify({ accessToken: 'legacy', refreshToken: 'legacy' })));
  await page.route('**/api/auth/refresh', route => route.fulfill({ status: 401 }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sign In', exact: true })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('uno-account-session'))).toBeNull();
});

test('unauthorized account request rotates once and retries', async ({ page }) => {
  let refreshes = 0;
  await page.route('**/api/auth/refresh', route => route.fulfill({ json: { ...session, accessToken: ++refreshes === 1 ? 'old-token' : 'new-token' } }));
  await page.route('**/api/auth/me', route => route.fulfill({ json: player }));
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Player profile' })).toBeVisible();
  await page.route('**/api/auth/collection', route => route.request().headers().authorization === 'Bearer new-token' ? route.fulfill({ json: [] }) : route.fulfill({ status: 401 }));
  await page.getByRole('button', { name: 'Player profile' }).click();
  await page.getByRole('button', { name: 'Profile & collections' }).click();
  await expect.poll(() => refreshes).toBe(2);
  expect(await page.evaluate(() => sessionStorage.getItem('uno-account-session'))).toBeNull();
});
