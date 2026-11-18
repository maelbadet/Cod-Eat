import {chromium} from '@playwright/test';
import {spawn,spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
const root=process.cwd(),pwd='Brand!7'+randomBytes(12).toString('hex'),url='http://localhost:5111';
const env={...process.env,ASPNETCORE_ENVIRONMENT:'Development',Demo__Sqlite:'true',ConnectionStrings__Demo:`Data Source=${path.join(root,'.qa','brand-'+Date.now()+'.db')}`,Bootstrap__Email:'brand@localhost.test',Bootstrap__Password:pwd,DataProtection__Path:path.join(root,'.qa/brand-keys'),Logging__LogLevel__Default:'Warning'};
const dll=path.join(root,'src/CodEat.Web/bin/Debug/net10.0/CodEat.Web.dll'),cwd=path.dirname(path.dirname(path.dirname(path.dirname(dll))));
const init=spawnSync('dotnet',[dll,'--bootstrap','--seed-demo'],{env,cwd:path.join(root,'src/CodEat.Web'),encoding:'utf8'});if(init.status!==0)throw new Error(init.stdout+init.stderr);
const log=fs.openSync('.qa/brand-server.log','w'),server=spawn('dotnet',[dll],{env:{...env,ASPNETCORE_URLS:url},cwd:path.join(root,'src/CodEat.Web'),stdio:['ignore',log,log]});
let browser;
try{
 browser=await chromium.launch({executablePath:process.env.CHROME_PATH,headless:true});
 const context=await browser.newContext({baseURL:url});const page=await context.newPage();
 for(let n=0;n<30;n++){try{await page.goto(url+'/Account/Login');break;}catch{await new Promise(r=>setTimeout(r,300));}}
 await page.getByLabel('Adresse e-mail',{exact:true}).fill('brand@localhost.test');await page.getByLabel('Mot de passe',{exact:true}).fill(pwd);await page.getByRole('button',{name:'Se connecter',exact:true}).click();await page.waitForURL(url+'/');
 for(const width of [320,390,768,1440]){await page.setViewportSize({width,height:1000});for(const tab of ['week','recipes','shopping','batch','settings']){await page.goto(url+'/?Tab='+tab);if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw new Error('Débordement '+tab+' '+width);if(tab==='week'&&[390,1440].includes(width)){if(width===1440){const positions=await page.locator('.day>.meal:nth-of-type(2)').evaluateAll(nodes=>nodes.map(n=>Math.round(n.getBoundingClientRect().top)));if(new Set(positions).size!==1)throw new Error('Créneaux non alignés');}await page.screenshot({path:`docs/qa/charte-semaine-${width}.png`,fullPage:true});}if(width===1440&&tab!=='week')await page.screenshot({path:`docs/qa/studio-${tab}.png`,fullPage:true});}}
 for(const width of [390,1440]){
 await page.setViewportSize({width,height:1000});await page.goto(url+'/?Tab=week');
 const card=page.locator('.day').nth(2).locator('.meal').first();await card.locator('summary').last().click();await card.locator('input[name="prepared"]').fill('4');await card.getByRole('button',{name:'Enregistrer ce repas'}).click();
 const edited=page.locator('.day').nth(2).locator('.meal').first();await edited.getByText('Portions / déplacer',{exact:true}).click();await edited.locator('input[name="portions"]').first().fill('3');await edited.getByRole('button',{name:'Modifier les portions'}).click();
 await page.goto(url+'/?Tab=shopping');const product=page.locator('.shoppingline').filter({hasText:'citron'}).first();await product.locator('.line-options>summary').click();await product.locator('input[name="quantity"]').fill('2');await product.getByRole('button',{name:'Enregistrer',exact:true}).click();
 let checkbox=page.locator('.product input').first();if(await checkbox.isChecked())await Promise.all([page.waitForNavigation(),checkbox.uncheck()]);await Promise.all([page.waitForNavigation(),page.locator('.product input').first().check()]);await page.reload();if(!await page.locator('.product input').first().isChecked())throw new Error('Achat non conservé');
 const reminder=page.locator('#verification form').first();if(await reminder.count())await reminder.getByRole('button',{name:'Ajouter aux courses',exact:true}).click();
 }
 await page.goto(url+'/?Tab=shopping');await page.locator('input[name="confirm"]').check();await page.getByRole('button',{name:'Finaliser une nouvelle version et créer les PDF'}).click();
 for(const [label,file] of [['Télécharger la liste de courses','courses'],['Télécharger le planning des repas','repas']]){const href=await page.locator('#exports').getByRole('link',{name:label,exact:true}).first().getAttribute('href');const response=await context.request.get(href);if(response.status()!==200)throw new Error('PDF indisponible');fs.writeFileSync(`docs/qa/charte-${file}.pdf`,await response.body());}
 console.log('Cinq écrans vérifiés à 320, 390, 768 et 1440 px ; créneaux alignés ; deux PDF éditoriaux générés.');
}finally{await browser?.close();server.kill();fs.closeSync(log);}



