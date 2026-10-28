async(page)=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.setViewportSize({width:688,height:656});
 await page.addInitScript(()=>{
  const listeners=[];window.sent=[];
  const task={id:'00000000-0000-0000-0000-000000000001',filename:'Coastal landscapes.mp4',host:'cdn.example.com',sourceUrl:'https://example.com/coast.mp4',destination:'C:\\Downloads\\Coastal landscapes.mp4',state:'Downloading',status:'Downloading',message:'',fileType:'Video',total:1200000000,received:408000000,rate:8600000,speed:'8.2 MB/s',remaining:'1m 42s',percent:34,progress:'408 MB of 1.2 GB',connections:2,segments:[],canPause:true,canResume:false,priority:0,canLimitSpeed:true,canUpdateLink:true};
  window.fixture={kind:'main',page:'dashboard',ready:true,tasks:[task,{...task,id:'00000000-0000-0000-0000-000000000002',filename:'blender.msi',fileType:'Other',percent:62},{...task,id:'00000000-0000-0000-0000-000000000003',filename:'Resources.zip',state:'Paused',canPause:false,canResume:true}],selectedId:task.id,expanded:false,currentRate:17200000,maxConnections:8,samples:[0,2000000,4000000,3000000,8000000,6500000,12000000,17200000],preferences:{folder:'C:\\Downloads',showFloatingWindow:true,floatingOnTop:true,historyDensity:'comfortable',historyGroupAttempts:true,typographyPreset:'inter',closeToTray:true,minimizeToTray:false},limits:{perFile:4,perSite:4,concurrent:3,retries:5,thresholdMiB:16,globalKiB:0},schedule:{enabled:false,start:'00:00',stop:'06:00',status:'Schedule off'},tools:{FFmpeg:'Available'},batch:{rows:[]}};
  window.publish=patch=>{Object.assign(fixture,patch);listeners.forEach(fn=>fn({data:{type:'model',model:fixture}}))};
  window.chrome={webview:{addEventListener:(name,fn)=>listeners.push(fn),postMessage:msg=>{sent.push(msg);if(msg.action==='ready')setTimeout(()=>publish({}),0);if(msg.action==='navigate')publish({page:msg.page});if(msg.action==='select')publish({selectedId:msg.id,expanded:msg.expanded??false});}}};
 });
 await page.route('**/DesktopAssets/app.js',route=>route.fulfill({path:'C:/Users/GH0STHUNT3R/Desktop/Dev/Syphon/src/Syphon.Desktop/DesktopAssets/app.js',contentType:'application/javascript'}));
 await page.goto('http://127.0.0.1:8768/src/Syphon.Desktop/DesktopAssets/index.html');
 await page.locator('.transfer').first().waitFor();
 if(await page.locator('.rail').count()!==1)throw Error('Missing live layout');if(await page.locator('.beta20-rail,.app-frame').count())throw Error('Legacy layout is still active');
 await page.getByRole('button',{name:'New download',exact:true}).click();await page.getByRole('button',{name:'Paste link',exact:true}).click();
 if(!(await page.evaluate(()=>sent.some(m=>m.action==='new'))))throw Error('New download bridge');
 await page.getByRole('button',{name:'Pause Coastal landscapes.mp4',exact:true}).first().click();
 if(!(await page.evaluate(()=>sent.some(m=>m.action==='pause'&&m.id))))throw Error('Pause bridge');
 await page.getByRole('button',{name:'Actions for Coastal landscapes.mp4',exact:true}).first().click();
 await page.locator('.task-menu').getByRole('button',{name:'Speed limit',exact:true}).click();
 await page.locator('#task-speed-limit').fill('512');await page.getByRole('button',{name:'Apply download speed limit',exact:true}).click();
 if(!(await page.evaluate(()=>sent.some(m=>m.action==='speed-limit'&&m.kibPerSecond===512))))throw Error('Speed bridge');
 await page.screenshot({path:'artifacts/beta20-main.png'});
 for(const name of ['History','Settings','Downloads']){await page.locator('.rail').getByRole('button',{name,exact:true}).click();await page.evaluate(()=>document.fonts.ready);if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Horizontal overflow '+name);await page.screenshot({path:'artifacts/beta20-'+name.toLowerCase()+'.png'});}
 await page.setViewportSize({width:860,height:820});
 await page.screenshot({path:'artifacts/beta20-rebuilt-main-final.png'});
 await page.getByRole('button',{name:'Actions for Coastal landscapes.mp4',exact:true}).click();
 await page.locator('.task-menu').getByRole('button',{name:'Speed limit',exact:true}).click();
 const quick=page.getByRole('button',{name:'Quick choice',exact:true});await quick.click();await quick.press('Escape');
 if(!await page.getByRole('dialog',{name:'Download speed limit',exact:true}).isVisible())throw Error('Dropdown Escape closed the dialog');
 await page.getByRole('dialog',{name:'Download speed limit',exact:true}).getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByRole('button',{name:'New download',exact:true}).click();await page.getByRole('button',{name:'Add links',exact:true}).click();
 await page.locator('#batch-links').fill('https://example.com/a.zip\nhttps://example.com/b.zip');await page.getByRole('button',{name:'Review links',exact:false}).click();
 await page.evaluate(()=>publish({batch:{id:sent.findLast(m=>m.action==='batch-preview').batchId,rows:[{id:'a',filename:'a.zip',url:'https://example.com/a.zip',valid:true,message:'Ready',bytes:1024},{id:'b',filename:'b.zip',url:'https://example.com/b.zip',valid:true,message:'Ready',bytes:2048}]}}));
 await page.getByRole('button',{name:'Add 2 downloads',exact:true}).waitFor();await page.screenshot({path:'artifacts/beta20-rebuilt-batch.png'});
 await page.getByRole('button',{name:'Add 2 downloads',exact:true}).click();if(!await page.evaluate(()=>sent.some(m=>m.action==='batch-add'&&m.immediately)))throw Error('Batch action lost');
 await page.getByRole('dialog',{name:'Batch downloads'}).getByRole('button',{name:'Close batch downloads'}).click();
 await page.evaluate(()=>publish({kind:'dialog',form:{url:'https://example.com/test.zip',filename:'Test.zip',folder:'C:\\Downloads',editable:true,single:false,remember:false,priority:0}}));
 await page.locator('#download-url').waitFor();await page.locator('#download-filename').fill('Changed.zip');await page.getByRole('button',{name:'Download now',exact:false}).click();
 if(!await page.evaluate(()=>sent.some(m=>m.action==='submit'&&m.form.filename==='Changed.zip'&&m.form.immediately)))throw Error('New download form lost data');
 for(const kind of ['remove','restart']){await page.evaluate(kind=>publish({kind:'confirmation',confirmation:kind,filename:'Test.zip',unfinished:true}),kind);await page.getByRole('dialog').waitFor();await page.screenshot({path:'artifacts/beta20-rebuilt-'+kind+'.png'});}
 await page.evaluate(()=>publish({kind:'connection',extensionFolder:'C:\\Program Files\\Syphon\\extensions'}));await page.getByRole('dialog',{name:'Connect your browser'}).waitFor();await page.screenshot({path:'artifacts/beta20-rebuilt-connection.png'});
 await page.evaluate(()=>publish({kind:'tray',active:2,paused:1,rate:33000000}));await page.getByRole('menu',{name:'Syphon tray commands'}).waitFor();await page.screenshot({path:'artifacts/beta20-rebuilt-tray.png'});
 if(errors.length)throw Error(errors.join('\n'));
 return {liveLayout:true,newAction:true,pauseAction:true,speedLimitAction:true,navigation:true,errors};
}
