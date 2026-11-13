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
  await hostPage.goto('/');
  await hostPage.getByLabel('Your display name').fill(host.name);
  await hostPage.getByLabel('Maximum players').selectOption('2');
  await hostPage.getByLabel('Starting rules').selectOption(ruleSet === 'house' ? '1' : '0');
  await hostPage.getByRole('button', { name: /Create private room/ }).click();
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(hostPage.getByText(ruleSet === 'house' ? 'House Rules' : 'Official Rules', { exact: true })).toBeVisible();
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

async function currentTurnIndex(players: TestPlayer[]): Promise<number> {
  await expect.poll(async () => {
    for (let index = 0; index < players.length; index++) {
      if (await players[index].page.getByRole('heading', { name: 'Your turn!' }).count()) return index;
    }
    return -1;
  }, { timeout: 15_000 }).toBeGreaterThanOrEqual(0);

  for (let index = 0; index < players.length; index++) {
    if (await players[index].page.getByRole('heading', { name: 'Your turn!' }).count()) return index;
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

async function waitForTurnToMove(player: TestPlayer) {
  await expect(player.page.getByRole('heading', { name: /^Waiting on / })).toBeVisible({ timeout: 15_000 });
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
    await expect(nextPlayer.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible({ timeout: 45_000 });

    await disconnected.context.setOffline(false);
    await expect(disconnected.page.getByText('Reconnected to the room.', { exact: true })).toBeVisible({ timeout: 60_000 });
    await expect(disconnected.page.locator('.hand-card')).toHaveCount(originalHandSize + 1);
    await expect(disconnected.page.getByRole('heading', { name: 'Waiting on ' + nextPlayer.name })).toBeVisible();
    await expect(nextPlayer.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await Promise.all(players.slice(1).map(player => player.context.close()));
  }
});

test('a House Rules Draw Two penalty is automatically drawn and skipped when its recipient times out', async ({ browser, page }) => {
  const players = await startTwoPlayerGame(browser, page, 'house');
  try {
    let penaltyRecipientIndex = -1;

    // Find and play a legal +2 from a real shuffled hand. The fixture never
    // reads the other player's hidden hand and never submits an illegal card.
    for (let attempt = 0; attempt < 180; attempt++) {
      const actorIndex = await currentTurnIndex(players);
      const actor = players[actorIndex];
      const handSize = await actor.page.locator('.hand-card').count();
      const drawTwoIndex = handSize > 1 ? await findPlayableDrawTwo(actor.page) : -1;

      if (drawTwoIndex >= 0) {
        await actor.page.locator('.hand-card').nth(drawTwoIndex).click();
        if (handSize === 2) {
          const unoButton = actor.page.getByRole('button', { name: 'UNO!' });
          await expect(unoButton).toBeEnabled();
          await unoButton.click();
        }

        penaltyRecipientIndex = 1 - actorIndex;
        await expect(players[penaltyRecipientIndex].page.getByText(/Draw penalty · 2 cards/)).toBeVisible();
        break;
      }

      await actor.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
      await expect.poll(async () =>
        (await actor.page.locator('.pass-decision').count()) > 0 ||
        (await actor.page.getByRole('heading', { name: /^Waiting on / }).count()) > 0,
      { timeout: 12_000 }).toBeTruthy();
      if (await actor.page.locator('.pass-decision').count()) {
        await actor.page.getByRole('button', { name: 'Pass' }).click();
        await waitForTurnToMove(actor);
      }
    }

    expect(penaltyRecipientIndex).toBeGreaterThanOrEqual(0);
    const recipient = players[penaltyRecipientIndex];
    const offender = players[1 - penaltyRecipientIndex];
    const originalHandSize = await recipient.page.locator('.hand-card').count();

    // Penalty timeout draws the accumulated amount, not the normal one-card
    // timeout amount, and still skips the recipient's turn.
    await expect(offender.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible({ timeout: 40_000 });
    await expect(recipient.page.locator('.hand-card')).toHaveCount(originalHandSize + 2, { timeout: 10_000 });
    await expect(offender.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
    await expect(recipient.page.getByText(/Draw penalty/)).toHaveCount(0);
  } finally {
    await Promise.all(players.slice(1).map(player => player.context.close()));
  }
});
