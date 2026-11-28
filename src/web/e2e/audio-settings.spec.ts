import {test,expect} from '@playwright/test';
test('audio preferences persist across reload',async({page})=>{
 await page.goto('/');
 await page.getByRole('button',{name:'Sign In as Guest'}).click();
 await page.getByRole('button',{name:'Game settings'}).click();
 await page.getByRole('switch',{name:/Sound effects/}).uncheck();
 await page.getByRole('switch',{name:/Background music/}).check();
 await page.getByRole('button',{name:'Visuals',exact:true}).click();
 await page.getByRole('switch',{name:/Enhanced animations/}).uncheck();
 await page.reload();
 const guest=page.getByRole('button',{name:'Sign In as Guest'});
 if(await guest.isVisible())await guest.click();
 await page.getByRole('button',{name:'Game settings'}).click();
 await expect(page.getByRole('switch',{name:/Sound effects/})).not.toBeChecked();
 await expect(page.getByRole('switch',{name:/Background music/})).toBeChecked();
 await page.getByRole('button',{name:'Visuals',exact:true}).click();
 await expect(page.getByRole('switch',{name:/Enhanced animations/})).not.toBeChecked();
});
