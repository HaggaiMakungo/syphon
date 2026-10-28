async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));const surfaces=[];
 await page.setViewportSize({width:1100,height:1000});
 for(const view of ['extension','video','tray','setup','connect']){
  await page.goto('http://127.0.0.1:8767/companion.html?view='+view);
  await page.evaluate(()=>document.fonts.ready);
  await page.screenshot({path:'design/beta20/html/companion-'+view+'.png',fullPage:true});
  await page.setViewportSize({width:360,height:800});
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Narrow overflow '+view);
  const core=page.locator('.dialog-core');if(await core.count()){
   const outer=await page.locator('.dialog-window').boundingBox(),inner=await core.boundingBox();
   if(Math.abs(outer.y+outer.height-inner.y-inner.height)>2)throw Error('Outer frame protrusion '+view);
  }
  await page.screenshot({path:'design/beta20/html/companion-mobile-'+view+'.png',fullPage:true});
  await page.setViewportSize({width:1100,height:1000});
  surfaces.push(view);
 }
 await page.goto('http://127.0.0.1:8767/companion.html?view=extension');
 await page.getByRole('checkbox',{name:'Use browser sign-in',exact:true}).check();
 await page.locator('summary').filter({hasText:'Privacy and limits'}).click();
 if(!await page.locator('.feature-status').textContent().then(s=>s.includes('No browser permissions')))throw Error('Feature preference');
 await page.getByRole('button',{name:'App unavailable',exact:true}).click();
 if(!await page.locator('.connection-chip').textContent().then(s=>s.includes('Check setup')))throw Error('Unavailable connection');
 await page.getByRole('button',{name:'Retry same request',exact:true}).click();
 if(!await page.locator('.feature-status').textContent().then(s=>s.includes('No sample reply')))throw Error('Retry state');
 await page.getByRole('button',{name:'App replied',exact:true}).click();
 if(!await page.getByRole('checkbox',{name:'Use browser sign-in',exact:true}).isChecked())throw Error('Feature state lost');
 await page.getByRole('button',{name:'Clear',exact:true}).click();
 if(!await page.locator('.handoff-card').textContent().then(s=>s.includes('No recent handoff')))throw Error('Clear handoff');
 await page.goto('http://127.0.0.1:8767/companion.html?view=video');
 await page.getByRole('radio',{name:'720p',exact:false}).check();
 await page.getByRole('button',{name:'Continue in Syphon',exact:true}).click();
 if(!await page.locator('.video-result p').textContent().then(s=>s.includes('720p')))throw Error('Video choice');
 const popupPromise=page.waitForEvent('popup');await page.getByRole('button',{name:'Open download options',exact:true}).click();
 const optionsPage=await popupPromise;await optionsPage.waitForLoadState();
 if(await optionsPage.getByRole('textbox',{name:'File name',exact:true}).inputValue()!=='Coastal landscapes.mp4')throw Error('Media handoff filename');
 if(!await optionsPage.locator('.file-preview').textContent().then(s=>s.includes('720p')))throw Error('Media handoff quality');
 await optionsPage.close();
 await page.getByRole('button',{name:'Choose another format',exact:true}).click();
 await page.getByRole('button',{name:'Audio',exact:true}).click();
 if(!await page.locator('.audio-art').textContent().then(s=>s.includes('MP3')))throw Error('Audio mode');
 await page.getByRole('button',{name:'Captions',exact:true}).click();
 await page.getByRole('combobox',{name:'Caption language',exact:true}).click();
 await page.getByRole('option',{name:'French',exact:false}).click();
 await page.getByRole('combobox',{name:'Caption format',exact:true}).click();
 await page.getByRole('option',{name:'WebVTT',exact:true}).click();
 await page.screenshot({path:'design/beta20/html/companion-video-captions.png',fullPage:true});
 await page.getByRole('button',{name:'Continue in Syphon',exact:true}).click();
 if(!await page.locator('.video-result p').textContent().then(s=>s.includes('French captions · VTT')))throw Error('Captions choice');
 await page.goto('http://127.0.0.1:8767/companion.html?view=tray');
 await page.getByRole('menuitem',{name:'Pause all',exact:true}).click();
 if(!await page.getByRole('menuitem',{name:'Pause all',exact:true}).isDisabled())throw Error('Pause tray');
 await page.getByRole('menuitem',{name:'Resume all',exact:true}).click();
 await page.getByRole('menuitem',{name:'Open Syphon',exact:false}).focus();await page.keyboard.press('End');
 if(await page.evaluate(()=>document.activeElement.dataset.action)!=='exit-tray')throw Error('Keyboard tray menu');
 await page.keyboard.press('Escape');
 if(await page.getByRole('menu',{name:'Syphon tray commands'}).count()!==0)throw Error('Escape tray');
 await page.getByRole('button',{name:'Toggle system tray menu',exact:true}).click();
 await page.getByRole('menuitem',{name:'Exit Syphon',exact:true}).click();
 await page.getByRole('button',{name:'Keep running',exact:true}).click();
 await page.getByRole('menuitem',{name:'Exit Syphon',exact:true}).click();
 await page.getByRole('button',{name:'Exit preview',exact:true}).click();
 if(!await page.locator('.tray-finished').textContent().then(s=>s.includes('would exit')))throw Error('Exit confirmation');
 await page.goto('http://127.0.0.1:8767/companion.html?view=setup');
 await page.getByRole('button',{name:'Continue',exact:true}).click();
 await page.getByRole('textbox',{name:'Application folder',exact:true}).fill('relative');
 await page.getByRole('button',{name:'Review installation',exact:true}).click();
 if(!await page.getByRole('alert').textContent().then(s=>s.includes('full application')))throw Error('Install path validation');
 await page.getByRole('textbox',{name:'Application folder',exact:true}).fill('D:\\Apps\\Syphon');
 await page.getByRole('button',{name:'Review installation',exact:true}).click();
 await page.getByRole('checkbox',{name:'Syphon running',exact:true}).check();
 if(!await page.getByRole('button',{name:'Install Syphon',exact:true}).isDisabled())throw Error('Running app blocker');
 await page.getByRole('checkbox',{name:'Syphon running',exact:true}).uncheck();
 await page.getByRole('button',{name:'Install Syphon',exact:true}).click();
 await page.getByRole('heading',{name:'Your workspace is ready',exact:true}).waitFor();
 await page.screenshot({path:'design/beta20/html/companion-setup-complete.png',fullPage:true});
 for(const mode of ['upgrade','repair','uninstall']){
  await page.goto('http://127.0.0.1:8767/companion.html?view=setup&mode='+mode);
  await page.screenshot({path:'design/beta20/html/companion-setup-'+mode+'.png',fullPage:true});
  if(mode==='upgrade'){
   await page.getByRole('button',{name:'Continue',exact:true}).click();await page.getByRole('button',{name:'Review installation',exact:true}).click();await page.getByRole('button',{name:'Upgrade Syphon',exact:true}).click();
  }else await page.getByRole('button',{name:mode==='repair'?'Repair installation':'Uninstall Syphon',exact:true}).click();
  await page.getByRole('heading',{name:mode==='upgrade'?'Upgrade preview complete':mode==='repair'?'Repair preview complete':'Syphon would be removed',exact:true}).waitFor();
 }
 await page.goto('http://127.0.0.1:8767/companion.html?view=connect');
 await page.getByRole('button',{name:'Edge',exact:true}).click();
 if(!await page.locator('.browser-address').textContent().then(s=>s.includes('edge://extensions')))throw Error('Edge instructions');
 await page.getByRole('button',{name:'App unavailable',exact:true}).click();
 await page.getByRole('button',{name:'Check connection',exact:true}).click();
 if(!await page.locator('.connection-result').textContent().then(s=>s.includes('No reply')))throw Error('Guide failure');
 await page.getByRole('button',{name:'App available',exact:true}).click();
 await page.getByRole('button',{name:'Check connection',exact:true}).click();
 if(!await page.locator('.connection-result').textContent().then(s=>s.includes('ready')))throw Error('Guide success');
 if(errors.length)throw Error(errors.join('\n'));
 await page.goto('http://127.0.0.1:8767/companion.html?view=extension');
 return {surfaces,extension:true,mediaModes:3,mediaHandoff:true,trayKeyboard:true,setupModes:4,runningAppBlocker:true,browserGuide:true,narrowOverflow:false,errors};
}
