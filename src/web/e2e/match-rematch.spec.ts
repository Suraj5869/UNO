import { reachHostUno } from './number-turns';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  return { name, context, page: await context.newPage() };
}

async function startTwoPlayerGame(browser: Browser, hostPage: Page): Promise<TestPlayer[]> {
  const host: TestPlayer = { name: 'Host', context: hostPage.context(), page: hostPage };
  await hostPage.setExtraHTTPHeaders({ 'X-Test-Deal': 'numbers' });
  await hostPage.goto('/');
  await hostPage.getByLabel('Your display name').fill(host.name);
  await hostPage.getByLabel('Maximum players').selectOption('2');
  await hostPage.getByRole('button', { name: /Create private room/ }).click();
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  const friend = await createPlayer(browser, 'Friend');
  await friend.page.goto('/');
  await friend.page.getByLabel('Your display name').fill(friend.name);
  await friend.page.getByLabel('Room code').fill(roomCode!);
  await friend.page.getByRole('button', { name: 'Join room' }).click();
  await expect(friend.page.getByRole('heading', { name: /Room/ })).toBeVisible();

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  await expect(hostPage.getByText('MATCH IN PROGRESS')).toBeVisible();
  await expect(friend.page.getByText('MATCH IN PROGRESS')).toBeVisible();
  return [host, friend];
}

async function finishMatch(players: TestPlayer[]) {
  const [winner, other] = players;
  await reachHostUno(players);
  await winner.page.getByRole('button', { name: 'UNO!' }).click();
  await other.page.locator('.hand-card').first().click();
  await other.page.getByRole('button', { name: 'UNO!' }).click();
  await winner.page.locator('.hand-card').first().click();

  await expect(winner.page.locator('.game-over-overlay')).toContainText(winner.name + ' wins!');
  await expect(other.page.locator('.game-over-overlay')).toContainText(winner.name + ' wins!');
}

test('the winner sees the match result and both players can opt into a fresh rematch lobby', async ({ browser, page }) => {
  const players = await startTwoPlayerGame(browser, page);
  try {
    const [winner, other] = players;
    await finishMatch(players);

    await other.page.getByRole('button', { name: 'Play Again' }).click();
    await expect(other.page.getByRole('heading', { name: /Room/ })).toBeVisible();
    await expect(other.page.getByRole('heading', { name: 'Players 1/2' })).toBeVisible();
    await expect(other.page.getByText('HOST', { exact: true })).toBeVisible();
    await expect(other.page.getByRole('button', { name: /Start game/ })).toBeDisabled();

    // The non-opted player must remain on the completed-match screen until
    // they explicitly choose to join the rematch.
    await expect(winner.page.locator('.game-over-overlay')).toBeVisible();
    await winner.page.getByRole('button', { name: 'Play Again' }).click();

    await expect(other.page.getByRole('heading', { name: 'Players 2/2' })).toBeVisible();
    await expect(winner.page.getByRole('heading', { name: 'Players 2/2' })).toBeVisible();
    await expect(winner.page.getByText('HOST', { exact: true })).toBeVisible();
    await other.page.getByRole('button', { name: /Start game/ }).click();

    for (const player of players) {
      await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
      await expect(player.page.locator('.hand-card')).toHaveCount(7);
    }
  } finally {
    await Promise.all(players.slice(1).map(player => player.context.close()));
  }
});

test('starting a rematch closes the previous non-returning player token', async ({ browser, page, request }) => {
  const players = await startTwoPlayerGame(browser, page);
  const [oldHost, newHost] = players;
  try {
    await finishMatch(players);
    const previous = await oldHost.page.evaluate(() => JSON.parse(sessionStorage.getItem('uno-room-access')!));
    await newHost.page.getByRole('button', { name: 'Play Again' }).click();
    await expect(newHost.page.getByRole('heading', { name: 'Players 1/2' })).toBeVisible();
    const url = 'http://localhost:7137/api/rooms/' + previous.room.code;
    expect((await request.post(url + '/join', { data: { displayName: 'New seat' } })).ok()).toBeTruthy();
    await newHost.page.getByRole('button', { name: /Start game/ }).click();
    await expect(newHost.page.locator('.hand-card')).toHaveCount(7);
    const response = await request.post(url + '/play-again', { data: { accessToken: previous.accessToken } });
    expect(response.status()).toBe(403);
  } finally {
    await newHost.context.close();
  }
});
