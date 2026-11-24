import { createPrivateRoom, submitRoomJoin } from './room-setup';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(150_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  return { name, context, page: await context.newPage() };
}

async function startTwoPlayerGame(
  browser: Browser,
  hostPage: Page,
  ruleSet: 'official' | 'house' = 'official',
): Promise<TestPlayer[]> {
  const host: TestPlayer = { name: 'Host', context: hostPage.context(), page: hostPage };
  await createPrivateRoom(hostPage, host.name, Number('2'), ruleSet === 'house' ? '1' : '0');
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(hostPage.getByText(ruleSet === 'house' ? 'House Rules' : 'Official Rules', { exact: true })).toBeVisible();
  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  const friend = await createPlayer(browser, 'Friend');
  await submitRoomJoin(friend.page, friend.name, roomCode!);
  await expect(friend.page.getByRole('heading', { name: /Room/ })).toBeVisible();

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  await expect(hostPage.locator('.table-felt')).toBeVisible();
  await expect(friend.page.locator('.table-felt')).toBeVisible();
  return [host, friend];
}

async function currentTurnIndex(players: TestPlayer[]): Promise<number> {
  await expect.poll(async () => {
    for (let index = 0; index < players.length; index++) {
      if (await players[index].page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true }).count()) return index;
    }
    return -1;
  }, { timeout: 15_000 }).toBeGreaterThanOrEqual(0);

  for (let index = 0; index < players.length; index++) {
    if (await players[index].page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true }).count()) return index;
  }
  throw new Error('No player received the active turn.');
}

async function findPlayableDrawTwo(page: Page): Promise<number> {
  return page.locator('.hand-card').evaluateAll(cards => {
    const topCard = document.querySelector('.top-card');
    if (!topCard) return -1;
    const topClasses = Array.from(topCard.classList);
    const activeColor = ['red', 'yellow', 'green', 'blue'].find(color => topClasses.includes(color));
    const topLabel = topCard.querySelector('b')?.textContent?.trim();
    return cards.findIndex(card => {
      if (card.querySelector('b')?.textContent?.trim() !== '+2') return false;
      return (!!activeColor && card.classList.contains(activeColor)) || topLabel === '+2';
    });
  });
}

test('a disconnected active player is timed out, then reconnects with the authoritative game state', async ({ browser, page }) => {
  const players = await startTwoPlayerGame(browser, page);
  try {
    const activeIndex = await currentTurnIndex(players);
    const disconnected = players[activeIndex];
    const nextPlayer = players[1 - activeIndex];
    const originalHandSize = await disconnected.page.locator('.hand-card').count();

    await disconnected.context.setOffline(true);

    // The server owns timeout enforcement. The connected opponent should see
    // the turn advance after 30s and the disconnected player draw one card.
    await expect(nextPlayer.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible({ timeout: 45_000 });

    await disconnected.context.setOffline(false);
    await expect(disconnected.page.getByText('Reconnected to the room.', { exact: true })).toBeVisible({ timeout: 60_000 });
    await expect(disconnected.page.locator('.hand-card')).toHaveCount(originalHandSize + 1);
    await expect(disconnected.page.locator('.opponent.is-turn strong').filter({ hasText: nextPlayer.name })).toBeVisible();
    await expect(nextPlayer.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
  } finally {
    await Promise.allSettled(players.slice(1).map(player => player.context.close()));
  }
});

test('a House Rules Draw Two penalty is automatically drawn and skipped when its recipient times out', async ({ browser, page }) => {
  await page.setExtraHTTPHeaders({ 'X-Test-Deal': 'drawTwo' });
  const players = await startTwoPlayerGame(browser, page, 'house');
  try {
    const [offender, recipient] = players;
    const drawTwoIndex = await findPlayableDrawTwo(offender.page);
    expect(drawTwoIndex).toBeGreaterThanOrEqual(0);
    await offender.page.locator('.hand-card').nth(drawTwoIndex).click();
    await expect(recipient.page.getByText(/Draw penalty.*2 cards/)).toBeVisible();
    const originalHandSize = await recipient.page.locator('.hand-card').count();

    // Penalty timeout draws the accumulated amount, not the normal one-card
    // timeout amount, and still skips the recipient's turn.
    await expect(offender.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible({ timeout: 40_000 });
    await expect(recipient.page.locator('.hand-card')).toHaveCount(originalHandSize + 2, { timeout: 10_000 });
    await expect(offender.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
    await expect(recipient.page.getByText(/Draw penalty/)).toHaveCount(0);
  } finally {
    await Promise.allSettled(players.slice(1).map(player => player.context.close()));
  }
});
