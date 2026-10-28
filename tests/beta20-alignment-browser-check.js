// Run with playwright-cli run-code against the existing Size Studio server.
async(page)=>{
 await page.goto('http://127.0.0.1:8769/preview.html');
 for(const viewport of [{width:760,height:580},{width:1536,height:864}]){
  await page.setViewportSize(viewport);const measurements=[];
  for(const name of ['dashboard','history','settings','dashboard']){
   await page.evaluate(name=>view(name),name);await page.waitForTimeout(220);
   if(await page.locator('.rail .nav-button svg').evaluateAll(nodes=>nodes.some(n=>n.getBoundingClientRect().width!==24||n.getBoundingClientRect().height!==24)))throw Error('Navigation glyph changed size');
   measurements.push(await page.evaluate(()=>Object.fromEntries(['.pixel-logo','.rail .add','.heading h1','.counts'].map(sel=>{const r=document.querySelector(sel).getBoundingClientRect();return[sel,{x:r.x,y:r.y}]}))));
   if(await page.locator('.rail .nav-button').count()!==2)throw Error('Current page should be omitted');
   if(await page.locator('.rail svg:not([data-icon-family="phosphor"])').count())throw Error('Non-Phosphor navigation');
   if(await page.locator('.content').evaluate(el=>getComputedStyle(el).transform)!=='none')throw Error('Page transition translates content');
  }
  for(const sel of Object.keys(measurements[0]))for(const value of measurements)if(Math.abs(value[sel].x-measurements[0][sel].x)>.5||Math.abs(value[sel].y-measurements[0][sel].y)>.5)throw Error('Page baseline differs: '+sel);
 }
 await page.evaluate(()=>document.title='PASS · stable page baselines and Phosphor navigation');
}
