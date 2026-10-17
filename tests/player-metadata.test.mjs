import {test} from 'node:test';
import assert from 'node:assert/strict';
import {sanitizePlayer,createPlayerMetadata,readYoutubePlayer,youtubeKey} from '../extensions/syphon/player-metadata.js';
import {downloadRequest} from '../extensions/syphon/handoff.js';
const id='YE7VzlLtp-4',url=`https://www.youtube.com/watch?v=${id}`;
const raw=()=>({videoId:id,title:'Fixture',duration:60,playable:true,formats:[
 {id:'137',height:1080,width:1920,qualityLabel:'1080p',codec:'video/mp4; codecs="avc1.640028"',size:1000,bitrate:2000},
 {id:'18',height:360,width:640,qualityLabel:'360p',codec:'video/mp4; codecs="avc1.42001e"',size:500,audio:true},
 {id:'999',height:2160,drm:true}],subtitles:[{url:'https://www.youtube.com/api/timedtext?v='+id+'&signature=secret',label:'English'},{url:'https://evil.test/sub.vtt',label:'Bad'}]});
test('page metadata is bounded, keeps quality IDs, excludes DRM and incomplete size estimates',()=>{
 const info=sanitizePlayer(raw(),id);assert.equal(info.qualities.length,2);assert.equal(info.qualities[0].youtubeFormat,'137');assert.equal(info.qualities[0].estimatedBytes,null);assert.equal(info.qualities[1].estimatedBytes,500);assert.equal(info.subtitles.length,1);
 assert.equal(sanitizePlayer({...raw(),videoId:'other'},id),null);assert.equal(sanitizePlayer({...raw(),live:true},id),null);assert.equal(sanitizePlayer({...raw(),playable:false},id),null);
});
test('cache survives a new worker instance without saving signed URLs and is scoped to the requesting video',async()=>{
 const state={};let calls=0;
 const api={storage:{session:{get:async k=>({[k]:structuredClone(state[k])}),set:async v=>Object.assign(state,structuredClone(v))}},scripting:{executeScript:async options=>{calls++;assert.equal(options.world,'MAIN');assert.deepEqual(options.target,{tabId:1,frameIds:[2]});return [{result:raw()}];}}};
 const sender={url,tab:{id:1},frameId:2};const a=await createPlayerMetadata(api)(url,sender);assert.equal(a.qualities[0].height,1080);assert(!JSON.stringify(state).includes('secret'));assert(!JSON.stringify(state).includes('timedtext'));
 api.scripting.executeScript=async()=>new Promise(()=>{});
 const b=await createPlayerMetadata(api)(url,sender);assert.equal(b.qualities[0].height,1080);
 assert.equal(await createPlayerMetadata(api)(url,{...sender,url:'https://other.test/'}),null);assert.equal(calls,1);
});
test('duplicate page inspections share work and extraction failures can fall back',async()=>{
 let resolve,calls=0;const api={storage:{session:{get:async()=>({}),set:async()=>{}}},scripting:{executeScript:()=>{calls++;return new Promise(r=>resolve=r);}}};
 const inspect=createPlayerMetadata(api),sender={url,tab:{id:1}};const a=inspect(url,sender),b=inspect(url,sender);await new Promise(r=>setTimeout(r,0));assert.equal(calls,1);resolve([{result:raw()}]);assert.equal((await a).qualities.length,2);assert.equal((await b).qualities.length,2);
 api.scripting.executeScript=async()=>{throw Error('unavailable');};assert.equal(await inspect(url,sender),null);
});
test('page URL checks reject deceptive hosts and credentials',()=>{
 assert.equal(youtubeKey('https://youtube.com.evil.test/watch?v='+id),null);assert.equal(youtubeKey('https://user:pass@youtube.com/watch?v='+id),null);assert.equal(youtubeKey('https://youtu.be/'+id),id);
});
test('advertised manifest height crosses the handoff without fake stream indexes',()=>{
 const request=downloadRequest('https://fixture.test/master.m3u8',undefined,{format:'mp4',qualityHeight:720});assert.equal(request.qualityHeight,720);assert.equal(request.videoStream,undefined);
 assert.throws(()=>downloadRequest(url,undefined,{format:'mp4',qualityHeight:720,youtubeFormat:'137'}));assert.throws(()=>downloadRequest(url,undefined,{format:'mp4',qualityHeight:1920}));
});

test('YouTube closed captions include auto-generated labels without changing player state',()=>{
 const previousDocument=globalThis.document,previousResponse=globalThis.ytInitialPlayerResponse;
 try {globalThis.document={querySelector:()=>null};globalThis.ytInitialPlayerResponse={videoDetails:{videoId:id},playabilityStatus:{status:'OK'},streamingData:{formats:[{itag:18,height:360,width:640,audioQuality:'AUDIO_QUALITY_MEDIUM'}]},captions:{playerCaptionsTracklistRenderer:{captionTracks:[{baseUrl:'https://www.youtube.com/api/timedtext?v='+id,languageCode:'en',kind:'asr',name:{runs:[{text:'English'}]}}]}}};const raw=readYoutubePlayer();assert.equal(raw.subtitles[0].label,'English (auto-generated)');assert.equal(sanitizePlayer(raw,id).subtitles.length,1);globalThis.ytInitialPlayerResponse.captions.playerCaptionsTracklistRenderer.captionTracks[0].name={simpleText:'English (auto-generated)'};assert.equal(readYoutubePlayer().subtitles[0].label,'English (auto-generated)');}
 finally{globalThis.document=previousDocument;globalThis.ytInitialPlayerResponse=previousResponse;}
});
