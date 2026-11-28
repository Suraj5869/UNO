import { test, expect } from '@playwright/test';

const player = { userId: 'reward-user', publicPlayerId: 'reward-player', email: 'reward@example.test', username: 'reward', displayName: 'Reward', avatarKey: '', level: 1, totalXp: 0, matchesPlayed: 0, wins: 0 };
const reward = { matchId: 'match-test', xp: 10, seasonalXp: 0 };

for (const scenario of ['pending', 'denied', 'exhausted', 'transient'] as const) {
  test('reward lifecycle: ' + scenario, async ({ page }) => {
    let requests = 0;
    await page.route('**/api/auth/refresh', route => route.fulfill({ json: { accessToken: 'test-token', expiresIn: 3600, player } }));
    await page.route('**/api/auth/me', route => route.fulfill({ json: { ...player, level: 2, totalXp: 10 } }));
    await page.route('**/api/progression/reward/match-test', route => {
      requests++;
      if (scenario === 'denied') return route.fulfill({ status: 404 });
      if (scenario === 'exhausted' || (scenario === 'pending' && requests === 1)) return route.fulfill({ status: 202, json: { pending: true } });
      if (scenario === 'transient' && requests === 1) return route.fulfill({ status: 503 });
      return route.fulfill({ json: reward });
    });
    await page.goto('/');
    await expect(page.getByRole('button', { name: 'Player profile' })).toBeVisible();
    await page.evaluate(async () => {
      const path = '/e2e/fixtures/reward-harness.tsx';
      const { mountReward } = await import(/* @vite-ignore */ path);
      mountReward();
    });
    const panel = page.locator('#reward-test-root');
    if (scenario === 'denied' || scenario === 'exhausted') {
      await expect(panel).toContainText('No saved reward yet', { timeout: 22000 });
      expect(requests).toBe(scenario === 'denied' ? 1 : 8);
    } else {
      await expect(panel).toContainText('Calculating your rewards');
      await expect(panel).toContainText('+10 lifetime XP', { timeout: 10000 });
      await expect(panel).toContainText('Level up!');
      expect(requests).toBe(2);
    }
  });
}
