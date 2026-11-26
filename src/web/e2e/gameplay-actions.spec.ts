import { createPrivateRoom, submitRoomJoin } from './room-setup';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(120_000);

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

async function startTwoPlayerGame(
  hostPage: Page,
  friend: TestPlayer,
) {
  await createPrivateRoom(hostPage, 'Host', Number('2'));

  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  await submitRoomJoin(friend.page, friend.name, roomCode!);
  await expect(friend.page.getByRole('heading', { name: /Room/ })).toBeVisible();

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  await expect(hostPage.locator('.table-felt')).toBeVisible();
  await expect(friend.page.locator('.table-felt')).toBeVisible();
  await expect(hostPage.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();

  return [
    { name: 'Host', page: hostPage },
    friend,
  ];
}

async function currentTurnIndex(players: Array<{ page: Page }>) {
  await expect.poll(async () => {
    for (let index = 0; index < players.length; index++) {
      if (await players[index].page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true }).count()) {
        return index;
      }
    }
    return -1;
  }, { timeout: 15_000 }).toBeGreaterThanOrEqual(0);

  for (let index = 0; index < players.length; index++) {
    if (await players[index].page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true }).count()) {
      return index;
    }
  }
  throw new Error('No player received the active turn.');
}

async function findPlayableNumberCard(page: Page) {
  return page.locator('.hand-card').evaluateAll((cards) => {
    const topCard = document.querySelector('.top-card');
    if (!topCard) return -1;

    const topClasses = topCard.className;
    const activeColor = ['red', 'yellow', 'green', 'blue']
      .find(color => topClasses.split(/\s+/).includes(color))
      ?? /selected-wild-color-(red|yellow|green|blue)/.exec(topClasses)?.[1];
    const topLabel = topCard.querySelector('b')?.textContent?.trim();

    return cards.findIndex(card => {
      const label = card.querySelector('b')?.textContent?.trim() ?? '';
      if (!/^\d$/.test(label)) return false;
      const cardClasses = card.className.split(/\s+/);
      return (!!activeColor && cardClasses.includes(activeColor)) || label === topLabel;
    });
  });
}

async function waitForDrawResolution(page: Page, otherPage: Page) {
  await expect.poll(async () =>
    await page.locator('.pass-decision').count() > 0 ||
    await otherPage.locator('.my-seat-label').getByText('YOUR TURN', { exact: true }).count() > 0,
  { timeout: 10_000 }).toBeTruthy();
}

test('a legal number card is played and the other client receives the next turn', async ({ browser, page }) => {
  const friend = await createPlayer(browser, 'Friend');
  const players = await startTwoPlayerGame(page, friend);

  try {
    // Keep the scenario independent of the shuffled deal: when a player has
    // no playable number card, draw; pass on playable non-number cards.
    for (let attempt = 0; attempt < 50; attempt++) {
      const turnIndex = await currentTurnIndex(players);
      const current = players[turnIndex];
      const other = players[1 - turnIndex];
      const cardIndex = await findPlayableNumberCard(current.page);

      if (cardIndex >= 0) {
        const handSize = await current.page.locator('.hand-card').count();
        await current.page.locator('.hand-card').nth(cardIndex).click();

        if (handSize === 2) {
          await expect(current.page.getByRole('button', { name: 'UNO!' })).toBeEnabled();
          await current.page.getByRole('button', { name: 'UNO!' }).click();
        }

        if (handSize === 1) {
          await expect(current.page.locator('.game-over-overlay')).toBeVisible();
        } else {
          await expect(other.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
        }

        await expect(current.page.locator('.hand-card')).toHaveCount(handSize - 1);
        return;
      }

      await current.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
      await waitForDrawResolution(current.page, other.page);

      if (await current.page.locator('.pass-decision').count()) {
        await current.page.getByRole('button', { name: 'Pass' }).click();
        await expect(other.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
      }
    }

    throw new Error('Could not reach a playable number card within 50 turns.');
  } finally {
    await friend.context.close();
  }
});

test('drawing cards updates the hand and passing a playable draw advances the turn', async ({ browser, page }) => {
  const friend = await createPlayer(browser, 'Friend');
  const players = await startTwoPlayerGame(page, friend);

  try {
    // A playable draw occurs naturally with repeated draws. Stop only when
    // the UI exposes the explicit pass decision, then verify it advances turn.
    for (let attempt = 0; attempt < 30; attempt++) {
      const turnIndex = await currentTurnIndex(players);
      const current = players[turnIndex];
      const other = players[1 - turnIndex];
      const originalHandSize = await current.page.locator('.hand-card').count();

      await current.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
      await expect.poll(async () =>
        await current.page.locator('.hand-card').count() === originalHandSize + 1,
      { timeout: 10_000 }).toBeTruthy();
      await waitForDrawResolution(current.page, other.page);

      if (await current.page.locator('.pass-decision').count()) {
        await current.page.getByRole('button', { name: 'Pass' }).click();
        await expect(other.page.locator('.my-seat-label').getByText('YOUR TURN', { exact: true })).toBeVisible();
        await expect(current.page.locator('.hand-card')).toHaveCount(originalHandSize + 1);
        return;
      }
    }

    throw new Error('No playable drawn card appeared within 30 draw attempts.');
  } finally {
    await friend.context.close();
  }
});

test('rapid draw clicks consume exactly one card', async ({ browser, page }) => {
  const friend = await createPlayer(browser, 'Friend');
  try {
    await startTwoPlayerGame(page, friend);
    const before = await page.locator('.hand-card').count();
    const draw = page.locator('.draw-pile-button');
    await expect(draw).toBeEnabled();
    await draw.evaluate(button => {
      (button as HTMLButtonElement).click();
      (button as HTMLButtonElement).click();
      (button as HTMLButtonElement).click();
    });
    await expect(page.locator('.hand-card')).toHaveCount(before + 1);
    await waitForDrawResolution(page, friend.page);
    await expect(friend.page.locator('.opponent').filter({ hasText: 'Host' })).toContainText(`${before + 1} cards`);
    await expect(page.locator('.hand-card')).toHaveCount(before + 1);
  } finally {
    await friend.context.close();
  }
});

test('two active rooms keep gameplay and hands isolated', async ({ browser, page }) => {
  const friend = await createPlayer(browser, 'Friend');
  const secondHost = await createPlayer(browser, 'Second host');
  const secondFriend = await createPlayer(browser, 'Second friend');
  try {
    await startTwoPlayerGame(page, friend);
    await startTwoPlayerGame(secondHost.page, secondFriend);
    const before = await page.locator('.hand-card').count();
    const secondBefore = await secondHost.page.locator('.hand-card').count();
    await Promise.all([
      page.locator('.draw-pile-button').click(),
      secondHost.page.locator('.draw-pile-button').click()
    ]);
    await expect(page.locator('.hand-card')).toHaveCount(before + 1);
    await expect(secondHost.page.locator('.hand-card')).toHaveCount(secondBefore + 1);
    await expect(friend.page.locator('.hand-card')).toHaveCount(7);
    await expect(secondFriend.page.locator('.hand-card')).toHaveCount(7);
    await expect(friend.page.locator('.opponent')).toHaveCount(1);
    await expect(secondFriend.page.locator('.opponent')).toHaveCount(1);
  } finally {
    await Promise.all([friend.context.close(), secondHost.context.close(), secondFriend.context.close()]);
  }
});
