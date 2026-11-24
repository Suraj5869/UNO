import { createPrivateRoom, submitRoomJoin } from './room-setup';
import { reachHostUno } from './number-turns';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { name, context, page };
}

async function startThreePlayerGame(browser: Browser, hostPage: Page): Promise<TestPlayer[]> {
  const players: TestPlayer[] = [{ name: 'Host', context: null as unknown as BrowserContext, page: hostPage }];
  await hostPage.setExtraHTTPHeaders({ 'X-Test-Deal': 'numbers' });
  await createPrivateRoom(hostPage, 'Host', Number('3'), '0');
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(hostPage.getByText('Official Rules', { exact: true })).toBeVisible();
  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  for (const name of ['Friend 1', 'Friend 2']) {
    const player = await createPlayer(browser, name);
    players.push(player);
    await submitRoomJoin(player.page, name, roomCode!);
    await expect(player.page.getByRole('heading', { name: /Room/ })).toBeVisible();
    await expect(player.page.getByText('Live connection')).toBeVisible();
  }

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  for (const player of players) await expect(player.page.locator('.table-felt')).toBeVisible();
  return players;
}

async function closePlayers(players: TestPlayer[]) {
  await Promise.allSettled(players.slice(1).map(player => player.context.close()));
}

test('a player can call UNO within the three-second grace period and all clients see it', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page);
  try {
    await reachHostUno(players);
    const actor = players[0];
    await expect(actor.page.locator('.hand-card')).toHaveCount(1);
    await expect(actor.page.getByRole('button', { name: 'UNO!' })).toBeEnabled();

    await actor.page.getByRole('button', { name: 'UNO!' }).click();
    for (const player of players) {
      await expect(player.page.locator('.uno-banner')).toHaveCount(0);
      await expect(player.page.locator('.uno-celebration-layer')).toContainText(actor.name + ' says UNO!');
    }
  } finally {
    await closePlayers(players);
  }
});

test('a different player can catch a missed UNO after the grace period and the target draws two', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page);
  try {
    await reachHostUno(players);
    const actor = players[0];
    const currentPlayer = players[1];
    const catcher = players[2];

    await expect(actor.page.locator('.hand-card')).toHaveCount(1);
    await expect(catcher.page.locator('.uno-banner')).toContainText(/waiting 3 seconds for UNO/i);
    const catchButton = catcher.page.getByRole('button', { name: 'Catch UNO (+2 cards)' });
    await expect(catchButton).toHaveCount(0);
    await expect(catchButton).toBeVisible({ timeout: 8_000 });

    // The server must reject a late UNO call even if nobody has caught the
    // player yet; the missed call remains available for another participant.
    await actor.page.getByRole('button', { name: 'UNO!' }).click();
    // SignalR hides Hub exception details in production, so the client receives
    // a generic invocation error. The still-visible catch action and successful
    // +2 draw below prove the late call was rejected and the missed call remains active.
    await expect(actor.page.getByRole('alert')).toBeVisible();
    await expect(catchButton).toBeVisible();

    await catchButton.click();

    // Catching is not a turn action: the target draws two and the current
    // player's turn remains active even when a third participant catches.
    await expect(actor.page.locator('.hand-card')).toHaveCount(3);
    await expect(currentPlayer.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
    for (const player of players) await expect(player.page.locator('.uno-banner')).toHaveCount(0);
  } finally {
    await closePlayers(players);
  }
});

test('a player holding only a Skip must draw instead of going out', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page);
  const [host, second, third] = players;
  try {
    await reachHostUno(players);
    await host.page.getByRole('button', { name: 'UNO!' }).click();
    for (const player of [second, third]) {
      await player.page.locator('.hand-card').first().click();
      await player.page.getByRole('button', { name: 'UNO!' }).click();
    }
    await host.page.locator('.draw-pile-button').click();
    // The ordered deck puts a playable Wild Draw Four on top of the draw pile.
    await host.page.getByRole('button', { name: 'Pass' }).click();
    await expect(second.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
    const lastCard = second.page.locator('.hand-card');
    await expect(lastCard).toHaveCount(1);
    await expect(lastCard).toContainText('SKIP');
    await expect(lastCard).toBeDisabled();
    await expect(second.page.getByText('Your last card must be a number', { exact: false })).toBeVisible();
    await second.page.locator('.draw-pile-button').click();
    await expect(second.page.locator('.hand-card')).toHaveCount(2);
  } finally {
    await closePlayers(players);
  }
});
