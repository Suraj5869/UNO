import { expect, type Page } from '@playwright/test';

// The ordered deal gives every seat a playable red number on these turns.
export async function reachHostUno(players: { page: Page }[]) {
  for (let round = 0; round < 6; round++) {
    for (const { page } of players) {
      await expect(page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
      await page.locator('.hand-card').first().click();
      await expect(page.locator('.hand-card')).toHaveCount(6 - round);
      if (round === 5 && page === players[0].page) return;
    }
  }
}
