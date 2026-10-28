async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));const states=[];
 for(const view of ['new','batch','replace','remove','restart']){
  await page.setViewportSize({width:1000,height:960});
  await page.goto('http://127.0.0.1:8767/dialogs.html?view='+view);
  await page.evaluate(()=>document.fonts.ready);
  await page.screenshot({path:'design/beta20/html/dialog-'+view+'.png',fullPage:true});
  if(view==='new'){
   await page.getByRole('textbox',{name:'Download link',exact:true}).fill('not a link');
   await page.getByRole('button',{name:'Download now',exact:true}).click();
   if(!await page.getByRole('alert').textContent().then(s=>s.includes('HTTP')))throw Error('URL validation');
   await page.getByRole('textbox',{name:'Download link',exact:true}).fill('https://example.com/file.zip');
   await page.getByRole('textbox',{name:'File name',exact:true}).fill('CON.zip');
   await page.getByRole('button',{name:'Download now',exact:true}).click();
   if(!await page.getByRole('alert').textContent().then(s=>s.includes('Windows')))throw Error('Filename validation');
   await page.getByRole('textbox',{name:'File name',exact:true}).fill('design.zip');
   await page.locator('summary').click();
   await page.getByRole('combobox',{name:'Connections',exact:true}).click();
   await page.screenshot({path:'design/beta20/html/dialog-new-options.png',fullPage:true});
   await page.getByRole('option',{name:'Single connection',exact:true}).click();
   await page.getByRole('combobox',{name:'Priority',exact:true}).click();
   await page.keyboard.press('End');await page.keyboard.press('Enter');
   await page.getByRole('button',{name:'Add to queue',exact:true}).click();
   if(await page.getByRole('heading',{name:'Added to your queue',exact:true}).count()!==1)throw Error('Queue');
  }
  if(view==='batch'){
   await page.getByRole('button',{name:'Review links',exact:false}).click();
   if(await page.locator('.review-row.valid').count()!==2||await page.locator('.review-row.duplicate').count()!==1||await page.locator('.review-row.invalid').count()!==1)throw Error('Batch classification');
   await page.screenshot({path:'design/beta20/html/dialog-batch-review.png',fullPage:true});
   await page.getByRole('checkbox',{name:'Start downloads immediately',exact:true}).uncheck();
   await page.getByRole('button',{name:'Add 2 downloads',exact:true}).click();
   if(await page.getByRole('heading',{name:'2 downloads queued',exact:true}).count()!==1)throw Error('Batch queue');
  }
  if(view==='replace'){
   await page.getByRole('button',{name:'Replace link',exact:true}).click();
   if(!await page.getByRole('alert').textContent().then(s=>s.includes('HTTP')))throw Error('Replace validation');
   await page.getByRole('textbox',{name:'New download link',exact:true}).fill('https://example.com/new.mp4');
   await page.getByRole('button',{name:'Replace link',exact:true}).click();
   if(await page.getByRole('heading',{name:'New link saved',exact:true}).count()!==1)throw Error('Replace confirmation');
  }
  if(view==='remove'){
   if(await page.getByRole('checkbox',{name:'Also delete unfinished data',exact:false}).isChecked())throw Error('Unsafe removal default');
   await page.getByRole('checkbox',{name:'Also delete unfinished data',exact:false}).check();
   if(!await page.locator('#remove-note').textContent().then(s=>s.includes('also be deleted')))throw Error('Removal consequence');
   await page.getByRole('button',{name:'Remove download',exact:true}).click();
   if(!await page.locator('.success p').textContent().then(s=>s.includes('would also be deleted')))throw Error('Remove result');
  }
  if(view==='restart'){
   await page.getByRole('button',{name:'Keep progress',exact:true}).click();
   if(!await page.locator('.success p').textContent().then(s=>s.includes('progress is kept')))throw Error('Keep progress');
   await page.getByRole('button',{name:'Back to dialog',exact:true}).click();
   await page.getByRole('button',{name:'Restart download',exact:true}).click();
   if(!await page.locator('.success p').textContent().then(s=>s.includes('reset to zero')))throw Error('Restart result');
  }
  await page.setViewportSize({width:360,height:800});
  await page.goto('http://127.0.0.1:8767/dialogs.html?view='+view);
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Mobile overflow '+view);
  await page.screenshot({path:'design/beta20/html/dialog-mobile-'+view+'.png',fullPage:true});
  await page.keyboard.press('Escape');
  if(await page.getByRole('heading',{name:'Nothing changed',exact:true}).count()!==1)throw Error('Escape '+view);
  states.push({view,interaction:true,cancel:true,narrowOverflow:false});
 }
 await page.goto('http://127.0.0.1:8767/dialogs.html?view=batch');
 await page.getByRole('textbox',{name:'One link per line',exact:true}).fill('bad\nbad');
 await page.getByRole('button',{name:'Review links',exact:false}).click();
 if(!await page.getByRole('button',{name:'Add 0 downloads',exact:true}).isDisabled())throw Error('All-invalid batch');
 await page.setViewportSize({width:1000,height:960});
 await page.goto('http://127.0.0.1:8767/dialogs.html?view=new');
 if(errors.length)throw Error(errors.join('\n'));
 return {states,allInvalidDisabled:true,joinedDropdowns:true,errors};
}
