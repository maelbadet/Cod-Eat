import { chromium } from '@playwright/test';
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import lighthouse from 'lighthouse';
import assert from 'node:assert/strict';
fs.mkdirSync('.qa',{recursive:true}); fs.mkdirSync('docs/qa',{recursive:true});
const root=process.cwd(), cert=path.join(root,'.qa/local.pfx'), db=path.join(root,'.qa',`browser-${Date.now()}.db`), password=`Qa!${randomBytes(18).toString('hex')}`;
const dll=path.join(root,'src/CodEat.Web/bin/Debug/net10.0/CodEat.Web.dll');
const env={...process.env,ASPNETCORE_ENVIRONMENT:'Development',Demo__Sqlite:'true',ConnectionStrings__Demo:`Data Source=${db}`,Bootstrap__Email:'qa@localhost.test',Bootstrap__Password:password,Bootstrap__SecondEmail:'second@localhost.test',Bootstrap__SecondPassword:password,BookStack__Simulated:'true',Logging__LogLevel__Default:'Warning',DataProtection__Path:path.join(root,'.qa/keys')};
const bootstrap=spawnSync('dotnet',[dll,'--bootstrap','--seed-demo'],{env,cwd:path.join(root,'src/CodEat.Web'),encoding:'utf8'});if(bootstrap.status!==0)throw new Error(bootstrap.stdout+bootstrap.stderr);
const log=fs.openSync('.qa/server.log','w');
const server=spawn('dotnet',[dll],{cwd:path.join(root,'src/CodEat.Web'),env:{...env,ASPNETCORE_ENVIRONMENT:'Production',Demo__AllowProduction:'true',ASPNETCORE_URLS:'https://localhost:7100;http://localhost:5100',ASPNETCORE_HTTPS_PORT:'7100',Kestrel__Certificates__Default__Path:cert},stdio:['ignore',log,log]});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_PATH,args:['--ignore-certificate-errors','--remote-debugging-port=9222']});
const results=[];
try{
 const context=await browser.newContext({ignoreHTTPSErrors:true,baseURL:"https://localhost:7100"}); const page=await context.newPage();
 for(let i=0;i<40;i++){try{await page.goto('https://localhost:7100/Account/Login');break;}catch{await new Promise(r=>setTimeout(r,500));}}
 await page.getByLabel('Adresse e-mail',{exact:true}).fill('qa@localhost.test');await page.getByLabel('Mot de passe',{exact:true}).fill(password);await page.getByRole('button',{name:'Se connecter',exact:true}).click();if(page.url().includes('/Account/Login')){console.log('Connexion QA :',await page.locator('.validation-summary-errors').allTextContents());}await page.waitForURL('https://localhost:7100/');
 assert.ok((await context.cookies()).some(c=>c.name.includes('Identity')&&c.secure&&c.httpOnly));results.push('Cookies de production Secure et HttpOnly vérifiés');
 await page.keyboard.press('Tab');assert.ok(await page.evaluate(()=>document.activeElement!==document.body));results.push('Navigation au clavier : focus atteint après Tab');
 const redirect=await context.request.get('http://localhost:5100/Account/Login',{maxRedirects:0,headers:{'X-Forwarded-Proto':'https'}});assert.equal(redirect.status(),307);assert.ok(redirect.headers().location.startsWith('https://'));results.push('HTTP redirigé vers HTTPS ; en-tête de proxy non approuvé ignoré');
 await page.goto('https://localhost:7100/?Tab=shopping');let markup=await page.content();
 for(let i=0;i<32;i++){const revision=/name="revision" value="([^"]+)"/.exec(markup)[1],token=/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/.exec(markup)[1];const response=await context.request.post('https://localhost:7100/?Tab=shopping',{form:{action:'manual',revision,__RequestVerificationToken:token,name:'Produit de démonstration '+i+' avec une dénomination particulièrement longue pour tester les listes chargées et les retours à la ligne',quantity:'1.25',unit:'kg',aisle:'Vérifications responsive'}});assert.equal(response.status(),200);markup=await response.text();}results.push('Liste chargée de 32 produits aux noms longs et quantités décimales');
 for(const width of [320,390,768,1440]){await page.setViewportSize({width,height:1000});for(const tab of ['week','recipes','shopping','batch','settings']){await page.goto(`https://localhost:7100/?Tab=${tab}`);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,`${tab} overflow at ${width}`);}await page.screenshot({path:`docs/qa/settings-${width}.png`,fullPage:true});results.push(`Cinq écrans sans débordement horizontal à ${width}px`);}
 for(const width of [390,1440]){
 await page.setViewportSize({width,height:1000});await page.goto('https://localhost:7100/');
 const add=page.locator('.day').nth(2).locator('.meal').first();await add.locator('summary').last().click();await add.locator('input[name="prepared"]').fill('4');await add.getByRole('button',{name:'Enregistrer ce repas'}).click();await page.waitForLoadState();
 const filled=page.locator('.day').nth(2).locator('.meal').first();await filled.getByText('Portions / déplacer',{exact:true}).click();await filled.locator('input[name="portions"]').first().fill('3');await filled.getByRole('button',{name:'Modifier les portions'}).click();await page.waitForLoadState();assert.ok((await page.locator('.day').nth(2).innerText()).includes('3'));
 await page.goto('https://localhost:7100/?Tab=shopping');const reminder=page.locator('#verification form').first();if(await reminder.count()){await reminder.getByRole('button',{name:'Ajouter aux courses',exact:true}).click();await page.waitForLoadState();}
 const checkbox=page.locator('.product input').first();if(await checkbox.isChecked()){await Promise.all([page.waitForNavigation(),checkbox.uncheck()]);}await Promise.all([page.waitForNavigation(),page.locator(".product input").first().check()]);await page.reload();assert.equal(await page.locator('.product input').first().isChecked(),true);
 await page.locator('input[name="confirm"]').check();await page.getByRole('button',{name:'Finaliser une nouvelle version et créer les PDF'}).click();await page.waitForLoadState();
 for(const label of ['Télécharger la liste de courses','Télécharger le planning des repas']){const link=page.locator('#exports').getByRole('link',{name:label,exact:true}).first();const response=await context.request.get(await link.getAttribute('href'));assert.equal(response.headers()['content-type'],'application/pdf');const bytes=await response.body();assert.equal(bytes.subarray(0,5).toString(),'%PDF-');fs.writeFileSync(`docs/qa/${width}-${label.includes('courses')?'courses':'repas'}.pdf`,bytes);}
 results.push(`Parcours réel à ${width}px : ajout repas, portions, rappel, achat persistant et deux PDF`);
 }
 await page.goto('https://localhost:7100/Admin/Recipes');await page.locator('input[name="catalog"]').check();await page.getByRole('button',{name:'Enregistrer l’ouverture'}).click();await page.waitForLoadState();
 const publicContext=await browser.newContext({ignoreHTTPSErrors:true,baseURL:"https://localhost:7100"});const publicPage=await publicContext.newPage();await publicPage.goto('https://localhost:7100/catalogue');const recipeUrl=await publicPage.locator('.cards a').first().getAttribute('href');await publicPage.goto(`https://localhost:7100${recipeUrl}`);const json=await publicPage.locator('script[type="application/ld+json"]').textContent();assert.equal(JSON.parse(json)['@type'],'Recipe');
 for(const desktop of [false,true])for(const route of ['/catalogue',recipeUrl]){const report=await lighthouse(`https://localhost:7100${route}`,{port:9222,output:'html',onlyCategories:['seo','accessibility'],logLevel:'error',...(desktop?{formFactor:'desktop',screenEmulation:{mobile:false,width:1440,height:1000,deviceScaleFactor:1,disabled:false}}:{})});const tag=`${route==='/catalogue'?'catalogue':'recette'}-${desktop?'desktop':'mobile'}`;fs.writeFileSync(`docs/qa/lighthouse-${tag}.html`,report.report);results.push(`${tag} : SEO ${report.lhr.categories.seo.score*100}/100, accessibilité ${report.lhr.categories.accessibility.score*100}/100`);}
 fs.writeFileSync('docs/qa/results.json',JSON.stringify(results,null,2));console.log(results.join('\n'));
}finally{await browser.close();server.kill();fs.closeSync(log);}


