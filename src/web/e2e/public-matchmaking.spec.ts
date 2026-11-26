import { test, expect } from '@playwright/test';

test('public queues isolate settings and start only at requested capacity', async ({ browser }) => {
  const contexts = await Promise.all(Array.from({length:4},()=>browser.newContext()));
  try {
    const pages=await Promise.all(contexts.map(c=>c.newPage()));
    async function search(i:number,count:string,rules:string) {
      const p=pages[i];
      await p.goto('/');
      await p.getByRole('button',{name:'Sign In as Guest'}).click();
      await p.getByLabel('Your display name').fill('Queue '+i);
      await p.getByRole('button',{name:/Ranked/}).click();
      await p.getByLabel('Players per match').selectOption(count);
      await p.getByLabel('Match rules').selectOption(rules);
      await p.getByRole('button',{name:/Find match/}).click();
    }
    await search(0,'3','0');
    await expect(pages[0].getByText('1 of 3 players found')).toBeVisible();
    await search(1,'3','1');
    await expect(pages[1].getByText('1 of 3 players found')).toBeVisible();
    await search(2,'3','0');
    await expect(pages[0].getByText('2 of 3 players found')).toBeVisible();
    await search(3,'3','0');
    for(const i of [0,2,3]) await expect(pages[i].locator('.table-felt')).toBeVisible();
    await pages[1].getByRole('button',{name:'Cancel search'}).click();
    await expect(pages[1].getByRole('button',{name:/Ranked/})).toBeVisible();
  } finally { await Promise.allSettled(contexts.map(c=>c.close())); }
});
