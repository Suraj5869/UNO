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
  let matchCompleted = false;
  await expect.poll(async () => {
    const completed = await Promise.all(
      players.map(player => player.page.locator('.game-over-overlay').count()),
    );
    if (completed.some(count => count > 0)) {
      matchCompleted = true;
      return 0;
    }

    for (let index = 0; index < players.length; index++) {
      if (await players[index].page.getByRole('heading', { name: 'Your turn!' }).count()) return index;
    }
    return -1;
  }, { timeout: 15_000 }).toBeGreaterThanOrEqual(0);

  if (matchCompleted) {
    throw new Error('The match completed before the UNO-call scenario was reached.');
  }

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
    const classes = Array.from(topCard.classList);
    const activeColor = ['red', 'yellow', 'green', 'blue'].find(color => classes.includes(color))
      ?? /selected-wild-color-(red|yellow|green|blue)/.exec(classes.join(' '))?.[1];
    const topLabel = topCard.querySelector('b')?.textContent?.trim();
    const label = card.querySelector('b')?.textContent?.trim() ?? '';
    if (!/^\d$/.test(label)) return false;
    const cardClasses = Array.from(card.classList);
    return (!!activeColor && cardClasses.includes(activeColor)) || label === topLabel;
  }, cardIndex);
}

async function findPlayableNumberCard(page: Page): Promise<number> {
  const count = await page.locator('.hand-card').count();
  for (let index = 0; index < count; index++) {
    if (await isPlayableNumberCard(page, index)) return index;
  }
  return -1;
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

    // Never play a final card while preparing the scenario. Doing so could
    // finish the random match before the UNO assertions run.
    if (playableIndex >= 0 && handSize > 1) {
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
      const drawnCardIndex = handSize;
      if (handSize > 1 && await isPlayableNumberCard(actor.page, drawnCardIndex)) {
        await actor.page.locator('.hand-card').nth(drawnCardIndex).click();
      } else {
        // Do not turn a one-card hand into an uncalled UNO state during setup.
        await actor.page.getByRole('button', { name: 'Pass' }).click();
      }
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
    // SignalR hides Hub exception details in production, so the client receives
    // a generic invocation error. The still-visible catch action and successful
    // +2 draw below prove the late call was rejected and the missed call remains active.
    await expect(actor.page.getByRole('alert')).toBeVisible();
    await expect(catchButton).toBeVisible();

    await catchButton.click();

    // Catching is not a turn action: the target draws two and the current
    // player's turn remains active even when a third participant catches.
    await expect(actor.page.locator('.hand-card')).toHaveCount(3);
    await expect(currentPlayer.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
    for (const player of players) await expect(player.page.locator('.uno-banner')).toHaveCount(0);
  } finally {
    await closePlayers(players);
  }
});
