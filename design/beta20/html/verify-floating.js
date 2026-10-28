async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 const output=[];
 for(const state of ['single-expanded','single-collapsed','multi-collapsed','multi-expanded']){
  await page.setViewportSize({width:1000,height:900});
  await page.goto('http://127.0.0.1:8767/floating.html?state='+state);
  await page.evaluate(()=>document.fonts.ready);
  const expanded=state.endsWith('-expanded');
  if(await page.getByRole('button',{name:(expanded?'Collapse':'Expand')+' monitor',exact:true}).count()!==1)throw Error('Incorrect initial state '+state);
  await page.screenshot({path:'design/beta20/html/floating-'+state+'.png',fullPage:true});
  await page.getByRole('button',{name:'Pause Coastal landscapes.mp4',exact:true}).click();
  if(await page.getByRole('button',{name:'Resume Coastal landscapes.mp4',exact:true}).count()!==1)throw Error('Pause '+state);
  await page.getByRole('button',{name:(expanded?'Collapse':'Expand')+' monitor',exact:true}).click();
  if(await page.getByRole('button',{name:(expanded?'Expand':'Collapse')+' monitor',exact:true}).count()!==1)throw Error('Toggle '+state);
  if(await page.getByRole('button',{name:'Resume Coastal landscapes.mp4',exact:true}).count()!==1)throw Error('State lost across toggle');
  await page.getByRole('button',{name:'Pin monitor',exact:true}).click();
  if(await page.getByRole('button',{name:'Unpin monitor',exact:true}).getAttribute('aria-pressed')!=='true')throw Error('Pin appearance');
  await page.getByRole('button',{name:'Close monitor',exact:true}).click();
  if(await page.locator('.monitor').count()!==0)throw Error('Close did not hide');
  await page.getByRole('button',{name:'Show monitor',exact:true}).click();
  if(await page.getByRole('button',{name:'Resume Coastal landscapes.mp4',exact:true}).count()!==1)throw Error('Close modified transfer');
  if(state.startsWith('multi')){
   await page.getByRole('button',{name:'Pause all',exact:true}).click();
   if(await page.getByRole('button',{name:'Resume all',exact:true}).count()!==1)throw Error('Pause all');
   await page.getByRole('button',{name:'Resume all',exact:true}).click();
   if(await page.getByRole('button',{name:'Pause all',exact:true}).count()!==1)throw Error('Resume all');
  }
  await page.setViewportSize({width:360,height:800});
  await page.goto('http://127.0.0.1:8767/floating.html?state='+state);
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Mobile overflow '+state);
  await page.screenshot({path:'design/beta20/html/floating-mobile-'+state+'.png',fullPage:true});
  output.push({state,toggle:true,pause:true,closePreservesState:true,narrowOverflow:false});
 }
 await page.setViewportSize({width:1100,height:950});
 await page.goto('http://127.0.0.1:8767/floating.html?state=multi-expanded');
 await page.getByRole('button',{name:'Details for blender.msi',exact:true}).click();
 if(await page.locator('.row-details').count()!==1)throw Error('Multiple detail panels');
 if(!await page.locator('.transfer-row.selected').textContent().then(s=>s.includes('blender.msi')))throw Error('Incorrect selected details');
 await page.goto('http://127.0.0.1:8767/floating.html?compare=1');
 await page.screenshot({path:'design/beta20/html/floating-comparison.png',fullPage:true});
 if(await page.locator('.monitor').count()!==4)throw Error('Missing comparison states');
 if(errors.length)throw Error(errors.join('\n'));
 return {states:output,selectedDetails:true,comparison:true,errors};
}
