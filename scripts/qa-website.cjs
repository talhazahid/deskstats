const {chromium}=require('C:/Users/talha/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const base=process.argv[2]||'http://127.0.0.1:3051/';
 fs.mkdirSync('artifacts',{recursive:true});
 try {
  const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
  for(const route of ['','privacy.html']) {
   const response=await page.goto(base+route,{waitUntil:'networkidle',timeout:60000});assert.equal(response.status(),200);
   await page.locator('img').evaluateAll(es=>Promise.all(es.map(e=>{e.loading='eager';return e.decode().catch(()=>{});}))); 
   assert.deepEqual(await page.locator('img').evaluateAll(es=>es.filter(e=>!e.naturalWidth).map(e=>e.src)),[]);
   for(const width of [320,390,768,1440]) {
    await page.setViewportSize({width,height:1000});
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),route+' overflows at '+width);
    const heavy=await page.locator('h1,h2,h3,.brand,.btn,summary').evaluateAll(es=>es.filter(e=>Number(getComputedStyle(e).fontWeight)>500).map(e=>e.tagName));assert.deepEqual(heavy,[]);
    if(!route){
     assert(await page.locator('.card,.step,.strip > div,.final .box').evaluateAll(es=>es.every(e=>{const s=getComputedStyle(e);return s.backgroundColor==='rgba(0, 0, 0, 0)'&&s.backgroundImage==='none'&&s.borderTopWidth==='0px'&&s.boxShadow==='none';})));
     assert.equal(await page.locator('.ico svg').count(),12);
    }
    if(width===390||width===1440)await page.screenshot({path:'artifacts/website-'+(route?'privacy':'home')+'-'+width+'.png',fullPage:true});
   }
   if(!route){
    assert.equal(await page.locator('a').evaluateAll(es=>es.filter(e=>e.getAttribute('href')==='privacy.html').length),2);
    await page.getByRole('tab',{name:'Sensors',exact:true}).click();
    await page.waitForFunction(()=>document.getElementById('shot').src.includes('shot-sensors.jpg'));
    await page.locator('#shot').evaluate(e=>e.decode());
    assert.equal(await page.getByRole('tab',{name:'Sensors',exact:true}).getAttribute('aria-selected'),'true');
    await page.keyboard.press('ArrowRight');assert.equal(await page.getByRole('tab',{name:'Resize',exact:true}).getAttribute('aria-selected'),'true');
    await page.getByText('Is DeskStats free?',{exact:true}).click();assert(await page.locator('details').first().getAttribute('open')!==null);
   }
  }
  assert.deepEqual(errors,[]);
  console.log('PASS: home and privacy, 320/390/768/1440px, all images, borderless sections, weights <=500, keyboard gallery, FAQ, privacy links, no browser errors.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
