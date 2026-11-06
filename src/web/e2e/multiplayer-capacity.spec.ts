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

test('ten independent players can join, an eleventh is rejected, and the current player can leave mid-match', async ({ browser }) => {
  const players: TestPlayer[] = [];

  try {
    const host = await createPlayer(browser, 'Player 1');
    players.push(host);

    await host.page.goto('/');
    await host.page.getByLabel('Your display name').fill(host.name);
    await host.page.getByLabel('Maximum players').selectOption('10');
    await host.page.getByRole('button', { name: /Create private room/ }).click();

    await expect(host.page.getByRole('heading', { name: /Room/ })).toBeVisible();
    await expect(host.page.getByText('Live connection')).toBeVisible();

    const roomCode = (await host.page.locator('.code-inline').textContent())?.trim();
    expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

    for (let index = 2; index <= 10; index++) {
      const player = await createPlayer(browser, `Player ${index}`);
      players.push(player);
      await joinRoom(player, roomCode!);
      await expect(host.page.getByRole('heading', { name: new RegExp(`Players ${index}\\/10`) })).toBeVisible();
    }

    const eleventhPlayer = await createPlayer(browser, 'Player 11');
    players.push(eleventhPlayer);
    await eleventhPlayer.page.goto('/');
    await eleventhPlayer.page.getByLabel('Your display name').fill(eleventhPlayer.name);
    await eleventhPlayer.page.getByLabel('Room code').fill(roomCode!);
    await eleventhPlayer.page.getByRole('button', { name: 'Join room' }).click();
    await expect(eleventhPlayer.page.getByRole('alert')).toContainText(/full/i);

    await host.page.getByRole('button', { name: /Start game/ }).click();

    for (const player of players.slice(0, 10)) {
      await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
      await expect(player.page.locator('.hand-card')).toHaveCount(7);
      await expect(player.page.locator('.opponent')).toHaveCount(9);
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

    const remainingPlayers = players.slice(0, 10).filter(player => player !== currentPlayer);
    for (const player of remainingPlayers) {
      await expect(player.page.locator('.opponent')).toHaveCount(8);
      await expect(player.page.locator('.opponent strong', { hasText: currentPlayerName! })).toHaveCount(0);
      await expect(player.page.locator('.hand-card')).toHaveCount(7);
    }
  } finally {
    await Promise.all(players.map(player => player.context.close()));
  }
});
