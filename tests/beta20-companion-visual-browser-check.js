// Run in a fresh playwright-cli session; visual-only localhost fixtures, no browser install.
async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{
  const state={lastHandoff:{sourceHost:'example.com',sentAt:Date.now(),response:{status:'accepted',message:'The download is in your queue.'}},detectVideos:true,captureDownloads:true,useBrowserSignin:false};
  window.chrome={runtime:{id:'visual-fixture',getURL:p=>'/companion/'+p,onMessage:{addListener(){},removeListener(){}},sendMessage:async msg=>msg.type==='syphon-player-catalog'?{players:[],sources:[]}:msg.type==='syphon-media-sources'?{sources:[]}:{ok:true},sendNativeMessage:async()=>({status:'ready'})},storage:{local:{get:async keys=>Object.fromEntries((Array.isArray(keys)?keys:[keys]).map(k=>[k,state[k]])),set:async vals=>Object.assign(state,vals)},onChanged:{addListener(){},removeListener(){}}},permissions:{contains:async()=>true,request:async()=>true},action:{setBadgeText:async()=>{},setBadgeBackgroundColor:async()=>{},setTitle:async()=>{}}};
  const original=Element.prototype.attachShadow;
  Element.prototype.attachShadow=function(options){return original.call(this,{...options,mode:'open'})};
 });
 await page.route('**/companion/**',route=>{const rel=new URL(route.request().url()).pathname.split('/companion/')[1];return route.fulfill({path:'C:/Users/GH0STHUNT3R/Desktop/Dev/Syphon/extensions/syphon/'+rel})});
 await page.setViewportSize({width:400,height:680});await page.goto('http://127.0.0.1:8769/companion/popup.html');
 await page.getByText('Added to Syphon',{exact:true}).waitFor();await page.waitForTimeout(200);
 if(await page.locator('svg[data-icon-family="phosphor"]').count()!==7)throw Error('Popup sharedglyphs');
 await page.screenshot({path:'artifacts/beta20-compact-validation/visual-extension-popup.png'});
 await page.route('**/visual-video',route=>route.fulfill({contentType:'text/html',body:'<!doctype html><html><head><meta charset="utf-8"></head><body style="margin:0;background:#16131b"><video src="/sample.mp4" style="position:absolute;top:40px;left:30px;width:640px;height:360px;background:#211b2a"></video><script src="/companion/media-model.js"></script><script src="/companion/video-button.js"></script></body></html>'}));
 await page.setViewportSize({width:800,height:650});await page.goto('http://127.0.0.1:8769/visual-video');
 const trigger=page.locator('[data-syphon-video-button] .trigger'),panel=page.locator('[data-syphon-video-button] .panel');
 await trigger.waitFor();
 const closed=await page.locator("[data-syphon-video-button]").evaluate(el=>({width:el.getBoundingClientRect().width,radius:getComputedStyle(el.shadowRoot.querySelector(".toolbar")).borderBottomLeftRadius}));
 if(closed.width!==168||closed.radius!=="22px")throw Error("Closed pill proportions: "+JSON.stringify(closed));
 await page.screenshot({path:"artifacts/beta20-compact-validation/visual-extension-video-closed.png"});
 await trigger.click();await panel.waitFor();await page.waitForTimeout(200);
 if(await page.locator('[data-syphon-video-button] .video-context h1').evaluate(el=>getComputedStyle(el).fontSize)!=='14px')throw Error('Video heading size');
 const w=await page.locator('[data-syphon-video-button]').evaluate(el=>({trigger:el.shadowRoot.querySelector('.toolbar').getBoundingClientRect().width,panel:el.shadowRoot.querySelector('.panel').getBoundingClientRect().width}));
 if(w.panel!==280||Math.abs(w.trigger-w.panel)>1)throw Error('Joined launcher/panel widths');
 await page.locator('[data-syphon-video-button] .quality-choice').first().click();
 if(await page.locator('[data-syphon-video-button] .confirm').evaluate(el=>el.scrollWidth>el.clientWidth+1))throw Error('Continue action overflow');
 await page.screenshot({path:'artifacts/beta20-compact-validation/visual-extension-video.png'});
 await page.emulateMedia({reducedMotion:'reduce'});
 if(await panel.evaluate(el=>getComputedStyle(el).animationName)!=='none')throw Error('Video reducedmotion');
 await page.evaluate(()=>window.syphonVideoController.stop());
 if(await page.locator('[data-syphon-video-button]').count())throw Error('Video cleanup');
 if(errors.length)throw Error(errors.join('\n'));
 await page.evaluate(()=>document.title='PASS · popup, sharedglyphs, joinedvideo, heading, footer, reducedmotion, cleanup');
}
