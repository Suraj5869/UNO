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

async function startThreePlayerGame(hostPage: Page, ruleSet: 'official' | 'house') {
  const players: TestPlayer[] = [
    { name: 'Host', context: null as unknown as BrowserContext, page: hostPage },
  ];

  await hostPage.goto('/');
  await hostPage.getByLabel('Your display name').fill('Host');
  await hostPage.getByLabel('Maximum players').selectOption('3');
  await hostPage.getByLabel('Starting rules').selectOption(ruleSet === 'house' ? '1' : '0');
  await hostPage.getByRole('button', { name: /Create private room/ }).click();
  await expect(hostPage.getByRole('heading', { name: /Room/ })).toBeVisible();

  const roomCode = (await hostPage.locator('.code-inline').textContent())?.trim();
  expect(roomCode).toMatch(/^[A-Z0-9]{6}$/);

  const browser = (hostPage.context() as BrowserContext & { browser?: () => Browser }).browser?.();
  if (!browser) throw new Error('Could not resolve the browser for multiplayer test players.');

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

async function cardMatches(card: ReturnType<Page['locator']>, matcher: RegExp): Promise<boolean> {
  const label = (await card.locator('b').textContent())?.trim() ?? '';
  return matcher.test(label);
}

async function chooseWildColor(page: Page, color: 'Red' | 'Yellow' | 'Green' | 'Blue') {
  await expect(page.locator('.color-picker')).toBeVisible();
  await page.locator('.color-picker').getByRole('button', { name: color, exact: true }).click();
  await expect(page.locator('.color-picker')).toHaveCount(0);
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
  for (let attempt = 0; attempt < 160; attempt++) {
    const actorIndex = await currentTurnIndex(players);
    const actor = players[actorIndex];
    const otherPlayers = players.filter((_, index) => index !== actorIndex);
    const playable = actor.page.locator('.hand-card:not([disabled])');
    let target = playable.filter({ has: actor.page.locator('b', { hasText: matcher }) }).first();

    if (await target.count()) {
      const handSizeBeforePlay = await actor.page.locator('.hand-card').count();
      // Avoid ending the match while a test is trying to exercise a special card.
      if (handSizeBeforePlay === 1) {
        await actor.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
      } else {
        const activeColorBeforePlay = await activeColor(actor.page);
        const offenderHadMatchingColor = matcher === specialLabels.wildDrawFour
          ? await hasMatchingColorInHand(actor.page, activeColorBeforePlay)
          : false;
        await target.click();
        if (matcher === specialLabels.wild || matcher === specialLabels.wildDrawFour) {
          await chooseWildColor(actor.page, color);
        }
        if (handSizeBeforePlay === 2) {
          await actor.page.getByRole('button', { name: 'UNO!' }).click();
        }
        return { actorIndex, handSizeBeforePlay, activeColorBeforePlay, offenderHadMatchingColor };
      }
    } else {
      await actor.page.getByRole('button', { name: /Draw one card from the draw pile/ }).click();
    }

    await expect.poll(async () =>
      (await actor.page.locator('.pass-decision').count()) > 0 ||
      (await currentTurnIndex(players)) !== actorIndex,
    { timeout: 12_000 }).toBeTruthy();

    if (await actor.page.locator('.pass-decision').count()) {
      const drawnCard = actor.page.locator('.hand-card').last();
      if (await cardMatches(drawnCard, matcher) && !(await drawnCard.isDisabled())) {
        const handSizeBeforePlay = await actor.page.locator('.hand-card').count();
        const activeColorBeforePlay = await activeColor(actor.page);
        const offenderHadMatchingColor = matcher === specialLabels.wildDrawFour
          ? await hasMatchingColorInHand(actor.page, activeColorBeforePlay)
          : false;
        await drawnCard.click();
        if (matcher === specialLabels.wild || matcher === specialLabels.wildDrawFour) {
          await chooseWildColor(actor.page, color);
        }
        if (handSizeBeforePlay === 2) {
          await actor.page.getByRole('button', { name: 'UNO!' }).click();
        }
        return { actorIndex, handSizeBeforePlay, activeColorBeforePlay, offenderHadMatchingColor };
      }
      await actor.page.getByRole('button', { name: 'Pass' }).click();
    }
  }

  throw new Error(`Could not reach a playable card matching ${matcher} within 160 turns.`);
}

async function closePlayers(players: TestPlayer[]) {
  await Promise.all(players.slice(1).map(player => player.context.close()));
}

test('Skip advances over the next player', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'official');
  try {
    const result = await playTargetCard(players, specialLabels.skip);
    await expect(players[(result.actorIndex + 2) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
    await expect(players[(result.actorIndex + 1) % 3].page.getByRole('heading', { name: /Waiting on/ })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Reverse changes direction and sends play to the previous player', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'official');
  try {
    const result = await playTargetCard(players, specialLabels.reverse);
    await expect(players[result.actorIndex].page.locator('.direction-count strong')).toHaveText('↺');
    await expect(players[(result.actorIndex + 2) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Wild lets the player choose a color and passes the turn', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'official');
  try {
    const result = await playTargetCard(players, specialLabels.wild, 'Green');
    await expect(players[result.actorIndex].page.locator('.top-card')).toHaveClass(/selected-wild-color-green/);
    await expect(players[(result.actorIndex + 1) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Wild Draw Four can be accepted and applies four cards', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'official');
  try {
    const result = await playTargetCard(players, specialLabels.wildDrawFour, 'Red');
    const challengerIndex = (result.actorIndex + 1) % 3;
    const challenger = players[challengerIndex];
    const handSize = await challenger.page.locator('.hand-card').count();

    await challenger.page.getByRole('button', { name: 'Accept +4' }).click();
    await expect(challenger.page.locator('.hand-card')).toHaveCount(handSize + 4);
    await expect(players[(challengerIndex + 1) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});

test('Wild Draw Four challenge applies the correct outcome', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'official');
  try {
    const result = await playTargetCard(players, specialLabels.wildDrawFour, 'Yellow');
    const challengerIndex = (result.actorIndex + 1) % 3;
    const offender = players[result.actorIndex];
    const challenger = players[challengerIndex];
    const offenderHandSize = await offender.page.locator('.hand-card').count();
    const challengerHandSize = await challenger.page.locator('.hand-card').count();

    await challenger.page.getByRole('button', { name: 'Challenge' }).click();
    if (result.offenderHadMatchingColor) {
      await expect(offender.page.locator('.hand-card')).toHaveCount(offenderHandSize + 4);
      await expect(challenger.page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
    } else {
      await expect(challenger.page.locator('.hand-card')).toHaveCount(challengerHandSize + 6);
      await expect(players[(challengerIndex + 1) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
    }
  } finally {
    await closePlayers(players);
  }
});

test('House Rules Draw Two creates a penalty, permits same-type stacking, and can be accepted', async ({ browser, page }) => {
  const players = await startThreePlayerGame(page, 'house');
  try {
    const first = await playTargetCard(players, specialLabels.drawTwo);
    const secondIndex = (first.actorIndex + 1) % 3;
    const second = players[secondIndex];
    await expect(second.page.getByText(/Draw penalty · 2 cards/)).toBeVisible();

    const stackCard = second.page.locator('.hand-card:not([disabled])').filter({
      has: second.page.locator('b', { hasText: specialLabels.drawTwo }),
    }).first();

    if (await stackCard.count()) {
      await stackCard.click();
      await expect(players[(secondIndex + 1) % 3].page.getByText(/Draw penalty · 4 cards/)).toBeVisible();
    }

    const penaltyPlayerIndex = stackCard && await stackCard.count()
      ? (secondIndex + 1) % 3
      : secondIndex;
    const penaltyPlayer = players[penaltyPlayerIndex];
    const penaltyAmount = stackCard && await stackCard.count() ? 4 : 2;
    const handSize = await penaltyPlayer.page.locator('.hand-card').count();

    await penaltyPlayer.page.getByRole('button', { name: `Draw ${penaltyAmount}` }).click();
    await expect(penaltyPlayer.page.locator('.hand-card')).toHaveCount(handSize + penaltyAmount);
    await expect(players[(penaltyPlayerIndex + 1) % 3].page.getByRole('heading', { name: 'Your turn!' })).toBeVisible();
  } finally {
    await closePlayers(players);
  }
});
