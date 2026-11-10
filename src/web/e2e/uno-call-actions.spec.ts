import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = { name: string; context: BrowserContext; page: Page };
type UnoOpportunity = { actorIndex: number; nextPlayerIndex: number };

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { name, context, page };
}

async function startThreePlayerGame(browser: Browser, hostPage: Page): Promise<TestPlayer[]> {
  const players: TestPlayer[] = [{ name: 'Host', context: null as unknown as BrowserContext, page: hostPage }];
  await hostPage.goto('/');
  await hostPage.getByLabel('Your display name').fill('Host');
  await hostPage.getByLabel('Maximum players').selectOption('3');
  await hostPage.getByLabel('Starting rules').selectOption('0');
  await hostPage.getByRole('button', { name: /Create private room/ }).click();
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();
  await expect(hostPage.getByText('Official Rules', { exact: true })).toBeVisible();
  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  for (const name of ['Friend 1', 'Friend 2']) {
    const player = await createPlayer(browser, name);
    players.push(player);
    await player.page.goto('/');
    await player.page.getByLabel('Your display name').fill(name);
    await player.page.getByLabel('Room code').fill(roomCode!);
    await player.page.getByRole('button', { name: 'Join room' }).click();
    await expect(player.page.getByRole('heading', { name: /Room/ })).toBeVisible();
    await expect(player.page.getByText('Live connection')).toBeVisible();
  }

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  for (const player of players) await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
  return players;
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

async function findPlayableNumberCard(page: Page): Promise<number> {
  return page.locator('.hand-card').evaluateAll(cards => {
    const topCard = document.querySelector('.top-card');
    if (!topCard) return -1;
    const classes = Array.from(topCard.classList);
    const activeColor = ['red', 'yellow', 'green', 'blue'].find(color => classes.includes(color))
      ?? /selected-wild-color-(red|yellow|green|blue)/.exec(classes.join(' '))?.[1];
    const topLabel = topCard.querySelector('b')?.textContent?.trim();
    return cards.findIndex(card => {
      const label = card.querySelector('b')?.textContent?.trim() ?? '';
      if (!/^\d$/.test(label)) return false;
      const cardClasses = Array.from(card.classList);
      return (!!activeColor && cardClasses.includes(activeColor)) || label === topLabel;
    });
  });
}

async function waitForActorToFinishTurn(player: TestPlayer) {
  await expect(player.page.getByRole('heading', { name: /^Waiting on / }),
    'The previous actor must receive the authoritative turn update').toBeVisible({ timeout: 15_000 });
}

async function advanceUntilUnoOpportunity(players: TestPlayer[]): Promise<UnoOpportunity> {
  // Progress only with legal number cards, preserving the shuffled deal and
  // avoiding any test-only access to opponents' hidden hands.
  for (let attempt = 0; attempt < 240; attempt++) {
    const actorIndex = await currentTurnIndex(players);
    const actor = players[actorIndex];
    const handSize = await actor.page.locator('.hand-card').count();
    const playableIndex = await findPlayableNumberCard(actor.page);

    if (playableIndex >= 0) {
      await actor.page.locator('.hand-card').nth(playableIndex).click();
      await expect(actor.page.locator('.hand-card')).toHaveCount(handSize - 1);
      await waitForActorToFinishTurn(actor);
      if (handSize === 2) {
        await expect(actor.page.getByRole('button', { name: 'UNO!' })).toBeEnabled();
        return { actorIndex, nextPlayerIndex: (actorIndex + 1) % players.length };
      }
      continue;
    }

    await actor.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
    await expect.poll(async () =>
      (await actor.page.locator('.pass-decision').count()) > 0 ||
      (await actor.page.getByRole('heading', { name: /^Waiting on / }).count()) > 0,
    { timeout: 12_000 }).toBeTruthy();

    if (await actor.page.locator('.pass-decision').count()) {
      const drawnCard = actor.page.locator('.hand-card').last();
      const label = (await drawnCard.locator('b').textContent())?.trim() ?? '';
      if (/^\d$/.test(label) && !(await drawnCard.isDisabled())) await drawnCard.click();
      else await actor.page.getByRole('button', { name: 'Pass' }).click();
      await waitForActorToFinishTurn(actor);
    }
  }
  throw new Error('Could not reach a playable two-card hand within 240 turns.');
}

async function closePlayers(players: TestPlayer[]) {
  await Promise.all(players.slice(1).map(player => player.context.close()));
}

test('a player can call UNO within the three-second grace period and all clients see it', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page);
  try {
    const opportunity = await advanceUntilUnoOpportunity(players);
    const actor = players[opportunity.actorIndex];
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
    const opportunity = await advanceUntilUnoOpportunity(players);
    const actor = players[opportunity.actorIndex];
    const currentPlayer = players[opportunity.nextPlayerIndex];
    const catcher = players[(opportunity.actorIndex + 2) % players.length];

    await expect(actor.page.locator('.hand-card')).toHaveCount(1);
    await expect(catcher.page.locator('.uno-banner')).toContainText(/waiting 3 seconds for UNO/i);
    const catchButton = catcher.page.getByRole('button', { name: 'Catch UNO (+2 cards)' });
    await expect(catchButton).toHaveCount(0);
    await expect(catchButton).toBeVisible({ timeout: 8_000 });

    // The server must reject a late UNO call even if nobody has caught the
    // player yet; the missed call remains available for another participant.
    await actor.page.getByRole('button', { name: 'UNO!' }).click();
    await expect(actor.page.getByRole('alert')).toContainText(/three-second UNO call window has expired/i);
    await expect(catchButton).toBeVisible();

    await catchButton.click();

    // Catching is not a turn action: the target draws two and the current
    // player's turn remains active even when a third participant catches.
    await expect(actor.page.locator('.hand-card')).toHaveCount(3);
    await expect.poll(() => currentTurnIndex(players)).toBe(opportunity.nextPlayerIndex);
    for (const player of players) await expect(player.page.locator('.uno-banner')).toHaveCount(0);
    await expect(currentPlayer.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});
