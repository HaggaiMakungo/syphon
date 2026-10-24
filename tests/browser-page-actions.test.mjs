import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeLinks, filterLinks, audioRequest, submitLinks } from '../extensions/syphon/page-actions.js';
test('page links preserve signed URLs, remove duplicates and reject credentials', () => {
 const rows = normalizeLinks(['https://site/a.zip?token=a%2Fb', 'https://site/a.zip?token=a%2Fb', 'javascript:bad', 'https://u:p@site/a', 'https://site/manual.PDF']);
 assert.equal(rows.length, 2); assert.equal(rows[0].url, 'https://site/a.zip?token=a%2Fb');
 assert.equal(filterLinks(rows, 'pdf').length, 1); assert.equal(filterLinks(rows, '.zip')[0].url, rows[0].url);
});
test('audio uses existing MP3 options and rejects unsupported pages', () => {
 assert.equal(audioRequest({pageUrl:'https://www.youtube.com/watch?v=abcdefghijk'}).format,'mp3');
 assert.equal(audioRequest({srcUrl:'https://site/a.m3u8?token=x',mediaType:'video'}).showOptions,true);
 assert.throws(()=>audioRequest({pageUrl:'https://site/article'}));
});
test('batch submission stops on cancelled or uncertain handoff and preserves retry IDs',async()=>{
 const rows=normalizeLinks(['https://site/a.zip','https://site/b.zip','https://site/c.zip']); const seen=[];
 const send=async r=>{seen.push(r);return {status:seen.length===1?'accepted':'cancelled'};};
 const result=await submitLinks(rows,send); assert.equal(seen.length,2);assert.equal(result.stopped,true);assert.ok(seen.every(r=>r.showOptions));
 const retry=[];await submitLinks(rows.slice(1),async r=>{retry.push(r);return {status:'unavailable'};});assert.equal(retry[0].requestId,seen[1].requestId);
});

test('page extraction is user scoped, private windows and denied permission never inject', async()=>{
 const {openPageLinks}=await import('../extensions/syphon/page-actions.js'); let injected=0;
 const denied={permissions:{request:async()=>false},scripting:{executeScript:async()=>{injected++;}}};
 await assert.rejects(()=>openPageLinks(denied,{id:1,url:'https://site/page',incognito:true}));
 await assert.rejects(()=>openPageLinks(denied,{id:1,url:'https://site/page'}));assert.equal(injected,0);
 const state={};let opened;
 const api={permissions:{request:async p=>{assert.deepEqual(p,{permissions:['scripting']});return true;}},
 scripting:{executeScript:async p=>{assert.deepEqual(p.target,{tabId:1});return [{result:['https://site/a?sig=x','https://site/a?sig=x','file:///secret']}];}},
 storage:{session:{set:async v=>Object.assign(state,v),remove:async k=>delete state[k]}},
 runtime:{getURL:p=>'chrome-extension://test/'+p},tabs:{create:async v=>{opened=v.url;}}};
 await openPageLinks(api,{id:1,url:'https://site/page'});assert.match(opened,/page-links.html#page-links-/);
 const staged=Object.values(state)[0];assert.equal(staged.length,1);assert.equal(staged[0].sourcePage,'https://site/page');
 api.tabs.create=async()=>{throw Error('closed');};const count=Object.keys(state).length;
 await assert.rejects(()=>openPageLinks(api,{id:1,url:'https://site/page'}));assert.equal(Object.keys(state).length,count);
});
