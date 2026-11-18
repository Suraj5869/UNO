import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = {
  name: string;
  context: BrowserContext;
  page: Page;
};

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { name, context, page };
}

async function joinRoom(player: TestPlayer, roomCode: string) {
  await player.page.goto('/');
  await player.page.getByLabel('Your display name').fill(player.name);
  await player.page.getByLabel('Room code').fill(roomCode);
  await player.page.getByRole('button', { name: 'Join room' }).click();
  await expect(player.page.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(player.page.getByText('Live connection')).toBeVisible();
}

for (const capacity of [2, 4, 5, 6, 10]) {
  test(`a full ${capacity}-seat table fits both viewports, rejects another join and handles departure`, async ({ browser }) => {
    const players: TestPlayer[] = [];

    try {
      const host = await createPlayer(browser, 'Player 1');
      players.push(host);

      await host.page.goto('/');
      await host.page.getByLabel('Your display name').fill(host.name);
      await host.page.getByLabel('Maximum players').selectOption(String(capacity));
      await host.page.getByRole('button', { name: /Create private room/ }).click();

      await expect(host.page.getByRole('heading', { name: /Room/ })).toBeVisible();
      await expect(host.page.getByText('Live connection')).toBeVisible();

      const roomCode = (await host.page.locator('.code-inline').textContent())?.trim();
      expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

      for (let index = 2; index <= capacity; index++) {
        const player = await createPlayer(browser, `Player ${index}`);
        players.push(player);
        await joinRoom(player, roomCode!);
        await expect(host.page.getByRole('heading', { name: new RegExp(`Players ${index}\\/${capacity}`) })).toBeVisible();
      }

      const extraPlayer = await createPlayer(browser, `Player ${capacity + 1}`);
      players.push(extraPlayer);
      await extraPlayer.page.goto('/');
      await extraPlayer.page.getByLabel('Your display name').fill(extraPlayer.name);
      await extraPlayer.page.getByLabel('Room code').fill(roomCode!);
      await extraPlayer.page.getByRole('button', { name: 'Join room' }).click();
      await expect(extraPlayer.page.getByRole('alert')).toContainText(/full/i);

      await host.page.getByRole('button', { name: /Start game/ }).click();

      for (const player of players.slice(0, capacity)) {
        await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
        await expect(player.page.locator('.hand-card')).toHaveCount(7);
        await expect(player.page.locator('.opponent')).toHaveCount(capacity - 1);
      }

      // Check both layouts against a full table, including the scrollable seats.
      for (const viewport of [{ width: 390, height: 844 }, { width: 1280, height: 720 }]) {
        await host.page.setViewportSize(viewport);
        await expect(host.page.locator('.top-card')).toBeInViewport();
        await expect(host.page.locator('.draw-pile-button')).toBeInViewport();
        const seats = host.page.locator('.opponent');
        await seats.last().scrollIntoViewIfNeeded();
        await expect(seats.last()).toBeInViewport();
        const lastCard = host.page.locator('.hand-card').last();
        await lastCard.scrollIntoViewIfNeeded();
        await expect(lastCard).toBeInViewport();
        expect(await host.page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width);
      }

      // Resolve the active player from the host's own player-specific view.
      const hostHasTurn = await host.page.getByRole('heading', { name: 'Your turn!' }).count();
      const currentPlayerName = hostHasTurn > 0
        ? host.name
        : (await host.page.locator('.opponent.is-turn strong').textContent())?.trim();

      expect(currentPlayerName).toBeTruthy();
      const currentPlayer = players.find(player => player.name === currentPlayerName);
      expect(currentPlayer).toBeDefined();

      // Leaving must remove the seat and publish the updated game state to all
      // remaining independent SignalR clients, not just update the leaver's UI.
      await currentPlayer!.page.getByRole('button', { name: 'Leave room' }).click();

      const remainingPlayers = players.slice(0, capacity).filter(player => player !== currentPlayer);
      for (const player of remainingPlayers) {
        if (capacity === 2) {
          await expect(player.page.locator('.game-over-overlay')).toBeVisible();
          continue;
        }
        await expect(player.page.locator('.opponent')).toHaveCount(capacity - 2);
        // Match the complete player name: "Player 1" must not match "Player 10".
        await expect(
          player.page.locator('.opponent strong').filter({ hasText: new RegExp(`^${currentPlayerName!}$`) }),
        ).toHaveCount(0);
        await expect(player.page.locator('.hand-card')).toHaveCount(7);
      }
    } finally {
      await Promise.all(players.map(player => player.context.close()));
    }
  });
}
