import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';

test.setTimeout(180_000);

type TestPlayer = {
  name: string;
  context: BrowserContext;
  page: Page;
};

type SpecialCardResult = {
  actorIndex: number;
  handSizeBeforePlay: number;
  activeColorBeforePlay: string;
  offenderHadMatchingColor: boolean;
  directionBeforePlay: 'clockwise' | 'counterclockwise';
};

const specialLabels: Record<string, RegExp> = {
  skip: /^SKIP$/,
  reverse: /^↻$/,
  drawTwo: /^\+2$/,
  wild: /^WILD$/,
  wildDrawFour: /^\+4$/,
};

async function createPlayer(browser: Browser, name: string): Promise<TestPlayer> {
  const context = await browser.newContext();
  const page = await context.newPage();
  return { name, context, page };
}

async function startThreePlayerGame(browser: Browser, hostPage: Page, ruleSet: 'official' | 'house', fixture: string) {
  const players: TestPlayer[] = [
    { name: 'Host', context: null as unknown as BrowserContext, page: hostPage },
  ];

  await hostPage.setExtraHTTPHeaders({ 'X-Test-Deal': fixture });
  await hostPage.goto('/');
  await hostPage.getByLabel('Your display name').fill('Host');
  await hostPage.getByLabel('Maximum players').selectOption('3');
  await hostPage.getByLabel('Starting rules').selectOption(ruleSet === 'house' ? '1' : '0');
  await hostPage.getByRole('button', { name: /Create private room/ }).click();
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();

  await expect(hostPage.getByText(ruleSet === 'house' ? 'House Rules' : 'Official Rules', { exact: true })).toBeVisible();

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
  }

  await hostPage.getByRole('button', { name: /Start game/ }).click();
  for (const player of players) {
    await expect(player.page.getByText('MATCH IN PROGRESS')).toBeVisible();
  }
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

async function waitForTurnChange(players: TestPlayer[], previousActorIndex: number): Promise<number> {
  // Wait for the player who submitted the action to receive the state update.
  // Scanning for any "Your turn!" heading can return a stale heading from that
  // same page and incorrectly hide the fact that its state has not changed.
  await expect(
    players[previousActorIndex].page.getByRole('heading', { name: /^Waiting on / }),
    'The previous actor should no longer own the active turn',
  ).toBeVisible({ timeout: 15_000 });

  return currentTurnIndex(players);
}

async function chooseWildColor(page: Page, color: 'Red' | 'Yellow' | 'Green' | 'Blue') {
  await expect(page.locator('.color-picker')).toBeVisible();
  await page.locator('.color-picker').getByRole('button', { name: color, exact: true }).click();
  await expect(page.locator('.color-picker')).toHaveCount(0);
}

async function currentDirection(page: Page): Promise<'clockwise' | 'counterclockwise'> {
  const glyph = (await page.locator('.direction-count strong').textContent())?.trim();
  if (glyph === '↻') return 'clockwise';
  if (glyph === '↺') return 'counterclockwise';
  throw new Error(`Unknown turn direction indicator: ${glyph}`);
}

function playerAtOffset(actorIndex: number, offset: number, direction: 'clockwise' | 'counterclockwise', playerCount: number): number {
  const step = direction === 'clockwise' ? 1 : -1;
  return (actorIndex + (offset * step) + playerCount) % playerCount;
}

async function activeColor(page: Page): Promise<string> {
  return page.locator('.top-card').evaluate(element => {
    const classes = Array.from(element.classList);
    const selected = classes.find(value => value.startsWith('selected-wild-color-'));
    if (selected) return selected.replace('selected-wild-color-', '');
    return ['red', 'yellow', 'green', 'blue'].find(color => classes.includes(color)) ?? 'unknown';
  });
}

async function hasMatchingColorInHand(page: Page, color: string): Promise<boolean> {
  return page.locator('.hand-card').evaluateAll((cards, active) =>
    cards.some(card => card.classList.contains(active as string) && !card.classList.contains('wild')),
    color,
  );
}

async function playTargetCard(
  players: TestPlayer[],
  matcher: RegExp,
  color: 'Red' | 'Yellow' | 'Green' | 'Blue' = 'Blue',
): Promise<SpecialCardResult> {
  const actorIndex = 0;
  const page = players[actorIndex].page;
  const activeColorBeforePlay = await activeColor(page);
  const directionBeforePlay = await currentDirection(page);
  const handSizeBeforePlay = await page.locator('.hand-card').count();
  const wild = matcher === specialLabels.wild || matcher === specialLabels.wildDrawFour;
  const cards = page.locator(wild ? '.hand-card' : '.hand-card.' + activeColorBeforePlay);
  const target = cards.filter({ has: page.locator('b', { hasText: matcher }) }).first();
  const offenderHadMatchingColor = await hasMatchingColorInHand(page, activeColorBeforePlay);
  await target.click();
  if (wild) await chooseWildColor(page, color);
  await expect(page.locator('.hand-card')).toHaveCount(handSizeBeforePlay - 1);
  await waitForTurnChange(players, actorIndex);
  return { actorIndex, handSizeBeforePlay, activeColorBeforePlay, offenderHadMatchingColor, directionBeforePlay };
}

async function closePlayers(players: TestPlayer[]) {
  await Promise.all(players.slice(1).map(player => player.context.close()));
}

test('Skip advances over the next player', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page, 'official', 'skip');
  try {
    const result = await playTargetCard(players, specialLabels.skip);
    const skippedIndex = playerAtOffset(result.actorIndex, 1, result.directionBeforePlay, players.length);
    const nextIndex = playerAtOffset(result.actorIndex, 2, result.directionBeforePlay, players.length);
    await expect(players[skippedIndex].page.getByRole('heading', { name: /Waiting on/ })).toBeVisible();
    await expect(players[nextIndex].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Reverse changes direction and sends play to the previous player', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page, 'official', 'reverse');
  try {
    const result = await playTargetCard(players, specialLabels.reverse);
    const expectedDirection = result.directionBeforePlay === 'clockwise' ? '↺' : '↻';
    const reverseNextIndex = playerAtOffset(result.actorIndex, 1, result.directionBeforePlay === 'clockwise' ? 'counterclockwise' : 'clockwise', players.length);
    await expect.poll(
      () => currentTurnIndex(players),
      { timeout: 15_000, message: 'Reverse should transfer the turn in the new direction' },
    ).toBe(reverseNextIndex);

    // Direction is shared game state; verify it reached every connected client.
    for (const player of players) {
      await expect(player.page.locator('.direction-count strong')).toHaveText(expectedDirection);
    }
  } finally {
    await closePlayers(players);
  }
});

test('Wild lets the player choose a color and passes the turn', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page, 'official', 'wild');
  try {
    const result = await playTargetCard(players, specialLabels.wild, 'Green');
    await expect(players[result.actorIndex].page.locator('.top-card')).toHaveClass(/selected-wild-color-green/);
    const nextIndex = playerAtOffset(result.actorIndex, 1, result.directionBeforePlay, players.length);
    await expect(players[nextIndex].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Wild Draw Four can be accepted and applies four cards', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page, 'official', 'legalFour');
  try {
    const result = await playTargetCard(players, specialLabels.wildDrawFour, 'Red');
    const challengerIndex = playerAtOffset(result.actorIndex, 1, result.directionBeforePlay, players.length);
    const challenger = players[challengerIndex];
    const handSize = await challenger.page.locator('.hand-card').count();

    await challenger.page.getByRole('button', { name: 'Accept +4' }).click();
    await expect(challenger.page.locator('.hand-card')).toHaveCount(handSize + 4);
    const followingIndex = playerAtOffset(challengerIndex, 1, result.directionBeforePlay, players.length);
    await expect(players[followingIndex].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

for (const fixture of ['legalFour', 'illegalFour']) {
  test(`Wild Draw Four challenge resolves a ${fixture} deal`, async ({ browser, page }) => {
    const players = await startThreePlayerGame(browser, page, 'official', fixture);
    try {
      const result = await playTargetCard(players, specialLabels.wildDrawFour, 'Yellow');
      const challengerIndex = playerAtOffset(result.actorIndex, 1, result.directionBeforePlay, players.length);
      const offender = players[result.actorIndex];
      const challenger = players[challengerIndex];
      const offenderHandSize = await offender.page.locator('.hand-card').count();
      const challengerHandSize = await challenger.page.locator('.hand-card').count();

      await challenger.page.getByRole('button', { name: 'Challenge' }).click();
      expect(result.offenderHadMatchingColor).toBe(fixture === 'illegalFour');
      if (result.offenderHadMatchingColor) {
        await expect(offender.page.locator('.hand-card')).toHaveCount(offenderHandSize + 4);
        await expect(challenger.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
      } else {
        await expect(challenger.page.locator('.hand-card')).toHaveCount(challengerHandSize + 6);
        const followingIndex = playerAtOffset(challengerIndex, 1, result.directionBeforePlay, players.length);
        await expect(players[followingIndex].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
      }
    } finally {
      await closePlayers(players);
    }
  });

}

test('House Rules Draw Two creates a penalty, permits same-type stacking, and can be accepted', async ({ browser, page }) => {
  const players = await startThreePlayerGame(browser, page, 'house', 'drawTwo');
  try {
    const first = await playTargetCard(players, specialLabels.drawTwo);
    const secondIndex = playerAtOffset(first.actorIndex, 1, first.directionBeforePlay, players.length);
    // The helper waits for the server's turn update before returning.
    await expect.poll(
      () => currentTurnIndex(players),
      { timeout: 15_000, message: 'Draw Two should make the next player active' },
    ).toBe(secondIndex);

    const second = players[secondIndex];
    await expect(second.page.getByText(/Draw penalty · 2 cards/)).toBeVisible();

    const stackCard = second.page.locator('.hand-card:not([disabled])').filter({
      has: second.page.locator('b', { hasText: specialLabels.drawTwo }),
    }).first();

    await expect(stackCard).toBeEnabled();
    await stackCard.click();
    const penaltyPlayerIndex = playerAtOffset(secondIndex, 1, first.directionBeforePlay, players.length);
    const penaltyPlayer = players[penaltyPlayerIndex];
    const penaltyAmount = 4;
    await expect(penaltyPlayer.page.getByText(/Draw penalty.*4 cards/)).toBeVisible();
    const handSize = await penaltyPlayer.page.locator('.hand-card').count();

    await penaltyPlayer.page.getByRole('button', { name: `Draw ${penaltyAmount}` }).click();
    await expect(penaltyPlayer.page.locator('.hand-card')).toHaveCount(handSize + penaltyAmount);
    const nextIndex = playerAtOffset(penaltyPlayerIndex, 1, first.directionBeforePlay, players.length);
    await expect(players[nextIndex].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});
