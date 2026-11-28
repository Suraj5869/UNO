import {test,expect} from '@playwright/test';
test('audio preferences persist across reload',async({page})=>{
 await page.goto('/');
 await page.getByRole('button',{name:'Sign In as Guest'}).click();
 await page.getByRole('button',{name:'Game settings'}).click();
 await page.getByLabel('Sound effects',{exact:true}).uncheck();
 await page.getByLabel('Background music',{exact:true}).check();
 await page.getByRole('button',{name:'Visuals',exact:true}).click();
 await page.getByLabel('Enhanced animations',{exact:true}).uncheck();
 await page.reload();
 const guest=page.getByRole('button',{name:'Sign In as Guest'});
 if(await guest.isVisible())await guest.click();
 await page.getByRole('button',{name:'Game settings'}).click();
 await expect(page.getByLabel('Sound effects',{exact:true})).not.toBeChecked();
 await expect(page.getByLabel('Background music',{exact:true})).toBeChecked();
 await page.getByRole('button',{name:'Visuals',exact:true}).click();
 await expect(page.getByLabel('Enhanced animations',{exact:true})).not.toBeChecked();
});
