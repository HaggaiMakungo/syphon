// Run with playwright-cli run-code (Get-Content -Raw this file).
// Requires the existing Size Studio server on127.0.0.1:8769.
async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.emulateMedia({reducedMotion:'no-preference'});
 await page.goto('http://127.0.0.1:8769/preview.html');await page.setViewportSize({width:760,height:580});
 await page.locator('.transfer').first().waitFor();await page.waitForTimeout(220);
 const active=page.locator('.progress.is-active').first();
 if(await active.evaluate(el=>getComputedStyle(el,':after').animationName)!=='orbital-transfer-light')throw Error('Active transferlight missing');
 await page.evaluate(()=>{window.motionTasks=[...model.tasks];publish({tasks:[{...motionTasks[0],state:'Completed'},...motionTasks.slice(1)]})});
 await page.waitForFunction(()=>document.body.dataset.celebrate==='true');
 await page.evaluate(()=>publish({tasks:motionTasks.filter(t=>t.state!=='Completed').slice(1)}));
 await page.waitForFunction(()=>document.body.dataset.celebrate==='false');
 await page.evaluate(()=>publish({tasks:[{...motionTasks[0],state:'Completed'},...motionTasks.slice(1)]}));
 await page.waitForFunction(()=>document.body.dataset.celebrate==='true');
 await page.waitForFunction(()=>document.body.dataset.celebrate==='false');
 await page.evaluate(()=>publish({tasks:[{...motionTasks[0],total:null,state:'Paused',canPause:false,canResume:true}]}));
 if(await page.locator('.unknown i').first().evaluate(el=>getComputedStyle(el).animationName)!=='none')throw Error('Paused transfer is moving');
 await page.evaluate(()=>publish({tasks:motionTasks}));await page.emulateMedia({reducedMotion:'reduce'});
 if(await active.evaluate(el=>getComputedStyle(el,':after').display)!=='none')throw Error('Reducedmotion transfer');
 await page.locator('[aria-label="Filter transfers"]').click();
 if(await page.locator('.joined-popover').evaluate(el=>getComputedStyle(el).animationName)!=='none')throw Error('Reducedmotion menu');
 await page.keyboard.press('Escape');await page.emulateMedia({reducedMotion:'no-preference'});
 await page.evaluate(()=>view('history'));await page.locator('.record-name').first().waitFor();await page.waitForTimeout(200);
 const old=await page.locator('.content').evaluateHandle(el=>el);
 await page.evaluate(()=>publish({currentRate:1}));
 if(!await page.locator('.content').evaluate((el,node)=>el===node,old))throw Error('Model tick remounted page');
 await page.evaluate(()=>view('dashboard'));await page.locator('.transfer').first().waitFor();
 if(await page.locator('.content').evaluate((el,node)=>el===node,old))throw Error('Navigation did not replace entry target');
 if(await page.locator('.content').evaluate(el=>getComputedStyle(el).animationName)!=='orbital-page-in')throw Error('Navigation motion missing');
 if(errors.length)throw Error(errors.join('\n'));
 await page.evaluate(()=>document.title='PASS · Orbital identity motion and reducedmotion');
}
