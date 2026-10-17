import { test } from 'node:test';
import assert from 'node:assert/strict';
import { probeDesktop, handleConnectionInstall } from '../extensions/syphon/connection.js';
function fixture(send) {
 const state={lastHandoff:{request:{requestId:'keep'}}},messages=[];
 return {state,messages,runtime:{sendNativeMessage:async(host,request)=>{messages.push({host,request});return send(request);}},storage:{local:{get:async key=>({[key]:state[key]}),set:async values=>Object.assign(state,structuredClone(values))}}};
}
test('fresh installation checks automatically; update and startup do not launch the app',async()=>{
 const api=fixture(r=>({version:1,requestId:r.requestId,status:'ready',message:'Ready'}));
 await handleConnectionInstall(api,{reason:'update'});await handleConnectionInstall(api);assert.equal(api.messages.length,0);
 await handleConnectionInstall(api,{reason:'install'});assert.equal(api.messages.length,1);assert.equal(api.messages[0].request.action,'ping');
 assert.equal(api.state.desktopConnection.status,'ready');assert.deepEqual(api.state.lastHandoff,{request:{requestId:'keep'}});
 assert.deepEqual(Object.keys(api.messages[0].request).sort(),['action','requestId','version']);
});
test('malformed, wrong-request, missing-host and timed-out replies fail without storing private errors',async()=>{
 for(const send of [()=>({}),()=>({version:1,requestId:'wrong',status:'ready',message:'Ready'}),()=>{throw new Error('private/path?secret=x');},()=>new Promise(()=>{})]) {
  const api=fixture(send);await probeDesktop(api,{timeoutMs:5});assert.equal(api.state.desktopConnection.status,'unavailable');assert.equal(JSON.stringify(api.state).includes('secret'),false);
 }
});
test('concurrent checks are deduplicated and newer saved checks win',async()=>{
 const api=fixture(async r=>{await new Promise(resolve=>setTimeout(resolve,5));return {version:1,requestId:r.requestId,status:'ready',message:'Ready'};});
 await Promise.all([probeDesktop(api),probeDesktop(api)]);assert.equal(api.messages.length,1);
 api.state.desktopConnection={checkedAt:Date.now()+10000,status:'unavailable'};await probeDesktop(api);assert.equal(api.state.desktopConnection.status,'unavailable');
});
