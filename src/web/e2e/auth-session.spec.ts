import { test, expect } from '@playwright/test';

const player = { userId: 'test-user', publicPlayerId: 'test-player', email: 'test@example.com', username: 'tester', displayName: 'Tester', avatarKey: '', level: 1, totalXp: 0, matchesPlayed: 0, wins: 0 };
const session = { accessToken: 'old-token', refreshToken: 'old-refresh', expiresIn: 3600, expiresAt: 0, player };

test.beforeEach(async ({ page }) => {
  await page.addInitScript(value => {
    sessionStorage.setItem('uno-account-session', JSON.stringify({ ...value, expiresAt: Date.now() + 3600000 }));
  }, session);
});

test('temporary profile outage preserves the account without refreshing', async ({ page }) => {
  let refreshes = 0;
  await page.route('**/api/auth/refresh', route => { refreshes++; return route.fulfill({ status: 503 }); });
  await page.route('**/api/auth/me', route => route.fulfill({ status: 503 }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sign In', exact: true })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => !!sessionStorage.getItem('uno-account-session'))).toBe(true);
  expect(refreshes).toBe(0);
});

test('unauthorized profile request rotates once and retries', async ({ page }) => {
  let refreshes = 0;
  await page.route('**/api/auth/refresh', route => {
    refreshes++;
    return route.fulfill({ json: { ...session, accessToken: 'new-token', refreshToken: 'new-refresh' } });
  });
  await page.route('**/api/auth/me', route => route.request().headers().authorization === 'Bearer new-token'
    ? route.fulfill({ json: player }) : route.fulfill({ status: 401 }));
  await page.goto('/');
  await expect.poll(() => page.evaluate(() => JSON.parse(sessionStorage.getItem('uno-account-session') || '{}').accessToken)).toBe('new-token');
  expect(refreshes).toBe(1);
});

test('invalid refresh token returns to sign in', async ({ page }) => {
  await page.route('**/api/auth/me', route => route.fulfill({ status: 401 }));
  await page.route('**/api/auth/refresh', route => route.fulfill({ status: 401 }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Sign In', exact: true })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('uno-account-session'))).toBeNull();
});
