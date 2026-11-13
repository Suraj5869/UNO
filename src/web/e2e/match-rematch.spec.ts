import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  return { name, context, page: await context.newPage() };
}

async function startTwoPlayerGame(browser: Browser, hostPage: Page): Promise<TestPlayer[]> {
  const host: TestPlayer = { name: 'Host', context: null as unknown as BrowserContext, page: hostPage };
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

async function isPlayableNumberCard(page: Page, cardIndex: number): Promise<boolean> {
  return page.locator('.hand-card').evaluateAll((cards, index) => {
    const topCard = document.querySelector('.top-card');
    const card = cards[index as number];
    if (!topCard || !card) return false;
    const topClasses = Array.from(topCard.classList);
    const activeColor = ['red', 'yellow', 'green', 'blue'].find(color => topClasses.includes(color))
      ?? /selected-wild-color-(red|yellow|green|blue)/.exec(topClasses.join(' '))?.[1];
    const topLabel = topCard.querySelector('b')?.textContent?.trim();
    const label = card.querySelector('b')?.textContent?.trim() ?? '';
    if (!/^\d$/.test(label)) return false;
    return (!!activeColor && card.classList.contains(activeColor)) || label === topLabel;
  }, cardIndex);
}

async function findPlayableNumberCard(page: Page): Promise<number> {
  const count = await page.locator('.hand-card').count();
  for (let index = 0; index < count; index++) {
    if (await isPlayableNumberCard(page, index)) return index;
  }
  return -1;
}

async function finishTurnAfterDraw(player: TestPlayer, handSizeBeforeDraw: number) {
  await expect.poll(async () =>
    (await player.page.locator('.pass-decision').count()) > 0 ||
    (await player.page.getByRole('heading', { name: /^Waiting on / }).count()) > 0,
  { timeout: 12_000 }).toBeTruthy();

  if (await player.page.locator('.pass-decision').count()) {
    const drawnIndex = handSizeBeforeDraw;
    if (handSizeBeforeDraw > 1 && await isPlayableNumberCard(player.page, drawnIndex)) {
      await player.page.locator('.hand-card').nth(drawnIndex).click();
    } else {
      await player.page.getByRole('button', { name: 'Pass' }).click();
    }
  }
  await expect(player.page.getByRole('heading', { name: /^Waiting on / })).toBeVisible({ timeout: 15_000 });
}

async function advanceToMatchWinner(players: TestPlayer[]): Promise<number> {
  let targetWinnerIndex: number | null = null;

  // Only number cards are played. The first player who reaches one card is
  // nominated as the winner; other players are never allowed to play their
  // final card. This keeps the browser scenario deterministic without exposing
  // or modifying any opponent's hidden hand.
  for (let attempt = 0; attempt < 600; attempt++) {
    const actorIndex = await currentTurnIndex(players);
    const actor = players[actorIndex];
    const handSize = await actor.page.locator('.hand-card').count();
    const playableIndex = await findPlayableNumberCard(actor.page);

    if (handSize === 1 && actorIndex === targetWinnerIndex && playableIndex >= 0) {
      await actor.page.locator('.hand-card').nth(playableIndex).click();
      for (const player of players) {
        await expect(player.page.locator('.game-over-overlay')).toBeVisible({ timeout: 15_000 });
      }
      return actorIndex;
    }

    if (handSize > 1 && playableIndex >= 0) {
      await actor.page.locator('.hand-card').nth(playableIndex).click();
      await expect(actor.page.locator('.hand-card')).toHaveCount(handSize - 1);
      if (handSize === 2) {
        if (targetWinnerIndex === null) targetWinnerIndex = actorIndex;
        const unoButton = actor.page.getByRole('button', { name: 'UNO!' });
        await expect(unoButton).toBeEnabled();
        await unoButton.click();
      }
      await expect(actor.page.getByRole('heading', { name: /^Waiting on / })).toBeVisible({ timeout: 15_000 });
      continue;
    }

    await actor.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
    await finishTurnAfterDraw(actor, handSize);
  }

  throw new Error('Could not complete a match within 600 legal number-card turns.');
}

test('the winner sees the match result and both players can opt into a fresh rematch lobby', async ({ browser, page }) => {
  const players = await startTwoPlayerGame(browser, page);
  try {
    const winnerIndex = await advanceToMatchWinner(players);
    const winner = players[winnerIndex];
    const other = players[1 - winnerIndex];

    await expect(winner.page.locator('.game-over-overlay')).toContainText(winner.name + ' wins!');
    await expect(other.page.locator('.game-over-overlay')).toContainText(winner.name + ' wins!');

    await winner.page.getByRole('button', { name: 'Play Again' }).click();
    await expect(winner.page.getByRole('heading', { name: /Room/ })).toBeVisible();
    await expect(winner.page.getByRole('heading', { name: 'Players 1/2' })).toBeVisible();
    await expect(winner.page.getByText('HOST', { exact: true })).toBeVisible();
    await expect(winner.page.getByRole('button', { name: /Start game/ })).toBeDisabled();

    // The non-opted player must remain on the completed-match screen until
    // they explicitly choose to join the rematch.
    await expect(other.page.locator('.game-over-overlay')).toBeVisible();
    await other.page.getByRole('button', { name: 'Play Again' }).click();

    await expect(winner.page.getByRole('heading', { name: 'Players 2/2' })).toBeVisible();
    await expect(other.page.getByRole('heading', { name: 'Players 2/2' })).toBeVisible();
    await expect(other.page.getByText('HOST', { exact: true })).toBeVisible();
    await winner.page.getByRole('button', { name: /Start game/ }).click();

    for (const player of players) {
      await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
      await expect(player.page.locator('.hand-card')).toHaveCount(7);
    }
  } finally {
    await Promise.all(players.slice(1).map(player => player.context.close()));
  }
});
