import {test} from 'node:test';
import assert from 'node:assert/strict';
import {normalizeLinks,filterLinks,audioRequest,submitLinks,openPageLinks} from '../extensions/syphon/page-actions.js';
test('page links keep eligible unique addresses unchanged and cap batch size',()=>{
 const signed='https://example.com/file.zip?token=a%2Fb';
 const rows=normalizeLinks([signed,signed,'javascript:void(0)','https://user:secret@example.com/private',...Array.from({length:210},(_,i)=>`https://example.com/${i}.jpg`)]);
 assert.equal(rows.length,200);assert.equal(rows[0].url,signed);assert.equal(new Set(rows.map(x=>x.requestId)).size,200);
 assert.equal(filterLinks(rows,'.jpg').length,199);assert.equal(filterLinks(rows,'file.zip').length,1);
});
test('audio action sends source context and rejects unrelated pages',()=>{
 const r=audioRequest({srcUrl:'https://cdn.test/video.mp4',pageUrl:'https://site.test/watch',mediaType:'video'});
 assert.equal(r.format,'mp3');assert.equal(r.showOptions,true);assert.equal(r.sourcePage,'https://site.test/watch');
 assert.equal(audioRequest({pageUrl:'https://www.youtube.com/watch?v=123'}).url,'https://www.youtube.com/watch?v=123');
 assert.throws(()=>audioRequest({pageUrl:'https://site.test/article'}));
});
test('batch stops after desktop cancellation and reuses stable request IDs',async()=>{
 const rows=normalizeLinks(['https://test/a','https://test/b','https://test/c']);const sent=[];
 const result=await submitLinks(rows,async r=>{sent.push(r);return {status:sent.length===1?'accepted':'cancelled',message:'Cancelled'};});
 assert.equal(result.completed,1);assert.equal(result.stopped,true);assert.equal(sent.length,2);assert.equal(sent[0].requestId,rows[0].requestId);
 assert.deepEqual(await submitLinks(rows,()=>assert.fail(),()=>true),{completed:0,stopped:true});
});
test('page links require user permission and clean session staging when review cannot open',async()=>{
 let staged,removed;const api={permissions:{request:async()=>true},scripting:{executeScript:async()=>[{result:['https://cdn.test/a.zip']}]},storage:{session:{set:async v=>staged=v,remove:async k=>removed=k}},runtime:{getURL:p=>'chrome-extension://fixture/'+p},tabs:{create:async()=>{throw Error('tab closed')}}};
 await assert.rejects(openPageLinks(api,{id:1,url:'https://site.test/',incognito:true}));
 await assert.rejects(openPageLinks(api,{id:1,url:'https://site.test/'}),/tab closed/);
 assert.equal(removed,Object.keys(staged)[0]);assert.equal(Object.values(staged)[0][0].sourcePage,'https://site.test/');
 api.permissions.request=async()=>false;await assert.rejects(openPageLinks(api,{id:1,url:'https://site.test/'}),/cancelled/);
});
