import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(90_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  return { name, context, page: await context.newPage() };
}

async function createRoom(host: TestPlayer, capacity = 3): Promise<string> {
  await host.page.goto('/');
  await host.page.getByLabel('Your display name').fill(host.name);
  await host.page.getByLabel('Maximum players').selectOption(String(capacity));
  await host.page.getByRole('button', { name: /Create private room/ }).click();
  await expect(host.page.getByRole('heading', { name: /Room/ })).toBeVisible();
  const roomCode = (await host.page.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);
  return roomCode!;
}

async function joinRoom(player: TestPlayer, roomCode: string) {
  await player.page.goto('/');
  await player.page.getByLabel('Your display name').fill(player.name);
  await player.page.getByLabel('Room code').fill(roomCode);
  await player.page.getByRole('button', { name: 'Join room' }).click();
  await expect(player.page.getByRole('heading', { name: /Room/ })).toBeVisible();
}

test('non-host UI and direct API requests cannot change rules or start a match', async ({ browser }) => {
  const host = await createPlayer(browser, 'Host');
  const friend = await createPlayer(browser, 'Friend');
  try {
    const roomCode = await createRoom(host, 2);
    await joinRoom(friend, roomCode);

    await expect(friend.page.getByRole('button', { name: /Start game/ })).toBeDisabled();
    await expect(friend.page.getByRole('radio', { name: /Official Rules/ })).toBeDisabled();
    await expect(friend.page.getByRole('radio', { name: /House Rules/ })).toBeDisabled();

    const access = await friend.page.evaluate(() =>
      JSON.parse(sessionStorage.getItem('uno-room-access') ?? 'null') as { accessToken: string },
    );
    expect(access?.accessToken).toBeTruthy();

    const rulesResponse = await friend.page.request.put(
      'http://localhost:7137/api/rooms/' + roomCode + '/rules',
      { data: { accessToken: access.accessToken, ruleSet: 1 } },
    );
    expect(rulesResponse.status()).toBe(403);

    const startResponse = await friend.page.request.post(
      'http://localhost:7137/api/rooms/' + roomCode + '/start',
      { data: { accessToken: access.accessToken } },
    );
    expect(startResponse.status()).toBe(403);

    await expect(host.page.getByText('Official Rules', { exact: true })).toBeVisible();
    await expect(host.page.getByText('Players 2/2')).toBeVisible();
  } finally {
    await Promise.all([host.context.close(), friend.context.close()]);
  }
});

test('the next participant becomes host when the creator leaves the lobby', async ({ browser }) => {
  const host = await createPlayer(browser, 'Original Host');
  const promoted = await createPlayer(browser, 'Promoted Host');
  const newcomer = await createPlayer(browser, 'New Player');
  try {
    const roomCode = await createRoom(host, 2);
    await joinRoom(promoted, roomCode);

    await host.page.getByRole('button', { name: 'Leave room' }).click();
    await expect(promoted.page.getByRole('heading', { name: 'Players 1/2' })).toBeVisible();
    await expect(promoted.page.getByText('HOST', { exact: true })).toBeVisible();

    await joinRoom(newcomer, roomCode);
    await expect(promoted.page.getByRole('heading', { name: 'Players 2/2' })).toBeVisible();
    await expect(promoted.page.getByRole('button', { name: /Start game/ })).toBeEnabled();
    await promoted.page.getByRole('button', { name: /Start game/ }).click();

    await expect(promoted.page.getByText('MATCH IN PROGRESS')).toBeVisible();
    await expect(newcomer.page.getByText('MATCH IN PROGRESS')).toBeVisible();
    await expect(promoted.page.locator('.hand-card')).toHaveCount(7);
    await expect(newcomer.page.locator('.hand-card')).toHaveCount(7);
  } finally {
    await Promise.all([host.context.close(), promoted.context.close(), newcomer.context.close()]);
  }
});

test('a new participant cannot join a room after the host starts the match', async ({ browser }) => {
  const host = await createPlayer(browser, 'Host');
  const friend = await createPlayer(browser, 'Friend');
  const visitor = await createPlayer(browser, 'Late Visitor');
  try {
    const roomCode = await createRoom(host, 3);
    await joinRoom(friend, roomCode);

    await host.page.getByRole('button', { name: /Start game/ }).click();
    await expect(host.page.getByText('MATCH IN PROGRESS')).toBeVisible();

    await visitor.page.goto('/');
    await visitor.page.getByLabel('Your display name').fill(visitor.name);
    await visitor.page.getByLabel('Room code').fill(roomCode);
    await visitor.page.getByRole('button', { name: 'Join room' }).click();
    await expect(visitor.page.getByRole('alert')).toContainText(/room|join|started|progress/i);
  } finally {
    await Promise.all([host.context.close(), friend.context.close(), visitor.context.close()]);
  }
});

test('the room API rejects a capacity outside the supported two-to-ten range', async ({ page }) => {
  const response = await page.request.post('http://localhost:7137/api/rooms', {
    data: { creatorDisplayName: 'Invalid Capacity', maximumPlayers: 11, ruleSet: 0 },
  });
  expect(response.status()).toBe(400);
});
