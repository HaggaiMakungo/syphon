async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.emulateMedia({reducedMotion:'no-preference'});
 await page.setViewportSize({width:1440,height:1000});
 await page.goto('http://127.0.0.1:8767/release.html');
 await page.evaluate(()=>document.fonts.ready);
 if(await page.locator('[role=tabpanel]:visible').count()!==1)throw Error('Expected one preview panel');
 await page.getByRole('tab',{name:'A little window, a lot of info'}).click();
 await page.locator('#panel-floating').waitFor({state:'visible'});
 await page.getByRole('tab',{name:'Still working? One glance.'}).click();
 await page.locator('#panel-taskbar').waitFor({state:'visible'});
 const state=await page.locator('#taskbar-icon').evaluate(el=>({state:el.state,duration:el.shadowRoot.querySelector('.charging').getAnimations()[0].effect.getTiming().duration}));
 if(state.state!=='downloading'||state.duration!==1000)throw Error('Wrong taskbar activity cadence');
 await page.getByRole('button',{name:'Pause',exact:true}).click();
 if(await page.locator('#taskbar-icon').evaluate(el=>el.state)!=='paused')throw Error('Pause failed');
 await page.getByRole('button',{name:'Resume',exact:true}).click();
 await page.getByRole('button',{name:'Finish',exact:true}).click();
 if(await page.locator('#taskbar-icon').evaluate(el=>el.state)!=='complete')throw Error('Completion failed');
 await page.getByRole('button',{name:'Replay',exact:true}).click();
 if(await page.locator('#taskbar-icon').evaluate(el=>el.state)!=='downloading')throw Error('Restart failed');
 await page.getByRole('tab',{name:'Still working? One glance.'}).focus();await page.keyboard.press('Home');
 if(await page.locator('#tab-queue').getAttribute('aria-selected')!=='true')throw Error('Keyboard tab selection failed');
 for(const button of await page.locator('[data-download]').all()){await button.click();await page.getByRole('dialog').waitFor({state:'visible'});await page.keyboard.press('Escape');}
 await page.locator('details').nth(1).locator('summary').click();
 if(await page.locator('details').nth(1).getAttribute('open')===null)throw Error('Release notes failed');
 for(const width of [1440,1024,768,390,360]){await page.setViewportSize({width,height:900});if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Overflow at '+width);}
 await page.screenshot({path:'design/beta20/html/release-mobile.png',fullPage:true});
 await page.emulateMedia({reducedMotion:'reduce'});
 if(await page.locator('#taskbar-icon').evaluate(el=>el.shadowRoot.querySelector('.charging').getAnimations().length)!==0)throw Error('Reduced motion ignored');
 await page.emulateMedia({reducedMotion:'no-preference'});await page.setViewportSize({width:1440,height:1000});await page.evaluate(()=>scrollTo(0,0));
 await page.screenshot({path:'design/beta20/html/release-desktop.png',fullPage:true});
 if(await page.locator('body').innerText().then(text=>/soft stack|design preview|design decisions|proposed|redesign/i.test(text)))throw Error('Internal design copy remains');
 const images=await page.locator('img').evaluateAll(els=>els.every(el=>el.complete&&el.naturalWidth>0));if(!images)throw Error('Broken image');
 if(errors.length)throw Error(errors.join('\n'));
 return {responsiveWidths:[360,390,768,1024,1440],previewTabs:true,keyboardNavigation:true,taskbarLoopMs:1000,pauseResumeComplete:true,downloadPreview:true,releaseNotes:true,reducedMotion:true,errors};
}
