// Check our local static release page in an isolated headless Edge profile.
// No browser automation dependency or user browser profile is used.
import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {randomUUID} from 'node:crypto';

const [url, browser, output, mode = 'preview'] = process.argv.slice(2);
const publicPage = mode === 'public';
if (!url || !browser || !output || !(/^http:\/\/127\.0\.0\.1:\d+\/index\.html$/.test(url) || (publicPage && /^https:\/\/HaggaiMakungo\.github\.io\/syphon\/(?:index\.html)?$/i.test(url)))) {
  throw new Error('Pass a loopback index.html URL, browser executable and output folder.');
}
await mkdir(output,{recursive:true});
const profile=path.resolve(output,'profile-'+randomUUID());
spawn(browser,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',
  '--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+profile,'about:blank'],
  {stdio:'ignore',windowsHide:true});
let connection;
const until=Date.now()+20000;
while(Date.now()<until){try{connection=(await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).trim().split('\n');break;}catch{await new Promise(r=>setTimeout(r,100));}}
if(!connection)throw new Error('Headless browser did not start.');
const ws=new WebSocket('ws://127.0.0.1:'+connection[0]+connection[1]);
await new Promise((resolve,reject)=>{ws.addEventListener('open',resolve,{once:true});ws.addEventListener('error',reject,{once:true});});
let next=0;const pending=new Map();
ws.addEventListener('message',e=>{const value=JSON.parse(e.data);const item=pending.get(value.id);if(item){pending.delete(value.id);clearTimeout(item.timer);value.error?item.reject(new Error(JSON.stringify(value.error))):item.resolve(value.result);}});
function call(method,params={},sessionId){return new Promise((resolve,reject)=>{const id=++next;const timer=setTimeout(()=>{pending.delete(id);reject(new Error('CDP timeout: '+method));},20000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));});}
const reports=[];
try{
  const {targetId}=await call('Target.createTarget',{url:'about:blank'});
  const {sessionId}=await call('Target.attachToTarget',{targetId,flatten:true});
  const evaluate=async expression=>{const result=await call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true},sessionId);if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));return result.result.value;};
  await call('Page.enable',{},sessionId);
  for(const [width,height] of [[1280,900],[760,540],[390,844]]){
    await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false},sessionId);
    await call('Page.navigate',{url},sessionId);
    await evaluate(`new Promise(resolve=>{const check=()=>{if(document.readyState==='complete'&&document.querySelector('h1'))document.fonts.ready.then(()=>resolve(true));else setTimeout(check,50);};check();})`);
    const report=await evaluate(`(()=>{
      const primary=document.querySelector('a.primary'),r=primary.getBoundingClientRect();
      return {width:innerWidth,scrollWidth:document.documentElement.scrollWidth,
        preview:document.querySelector('.preview').textContent.includes(${JSON.stringify(publicPage ? 'Public beta' : 'Not published')}),
        primaryHeight:r.height,primaryAboveFold:r.top>=0&&r.bottom<=innerHeight,fonts:document.fonts.check('400 14px Inter')&&document.fonts.check('500 30px Space'),
        brokenImages:[...document.images].filter(i=>!i.complete||!i.naturalWidth).length,
        duplicateIds:[...document.querySelectorAll('[id]')].map(e=>e.id).filter((id,i,a)=>a.indexOf(id)!==i),
        scriptTags:document.scripts.length};})()`);
    if(report.width!==width||report.scrollWidth>width+1||!report.preview||report.primaryHeight<44||!report.primaryAboveFold||!report.fonts||report.brokenImages||report.duplicateIds.length||report.scriptTags){
      throw new Error('Page assertion failed: '+JSON.stringify(report));
    }
    const {cssContentSize}=await call('Page.getLayoutMetrics',{},sessionId);
    const screenshot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,
      clip:{x:0,y:0,width,height:Math.ceil(cssContentSize.height),scale:1}},sessionId);
    await writeFile(path.join(output,width+'-page.png'),Buffer.from(screenshot.data,'base64'));
    reports.push(report);
  }
  const metadata=await(await fetch(new URL('release.json',url))).json();
  for(const item of [metadata.installer,metadata.extension,...metadata.sources,...(metadata.applicationSource?[metadata.applicationSource]:[])]){
    const response=await fetch(new URL(item.url||item.file,url),{method:'HEAD'});
    if(!response.ok||Number(response.headers.get('content-length'))!==item.bytes)throw new Error('Download link/length mismatch: '+item.file);
  }
  await call('Input.dispatchKeyEvent',{type:'keyDown',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},sessionId);
  await call('Input.dispatchKeyEvent',{type:'keyUp',key:'Tab',code:'Tab',windowsVirtualKeyCode:9},sessionId);
  if(!await evaluate("document.activeElement.classList.contains('skip')&&document.activeElement.getBoundingClientRect().top>=0"))throw new Error('Keyboard skip link is unavailable.');
  await writeFile(path.join(output,'report.json'),JSON.stringify({passed:true,reports,downloads:'HEAD status/size match',keyboard:'visible skip link'},null,2));
  console.log('PASS release page: 1280/760/390px, no horizontal overflow, fonts/images, download links and keyboard skip link.');
}finally{try{await call('Browser.close');}catch{}ws.close();}
