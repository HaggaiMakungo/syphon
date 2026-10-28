// Bounded title lookup; executed in the permitted page's isolated world.
export function pageTitleCandidates() {
 const values=[];
 const add=value=>{if(typeof value==='string'&&value.trim())values.push(value.trim().slice(0,512));};
 try {add(navigator.mediaSession?.metadata?.title);}catch{}
 for(const selector of ['meta[property="og:video:title"]','meta[property="og:title"]','meta[name="twitter:title"]']) add(document.querySelector(selector)?.content);
 const walk=(value,depth=0)=>{if(!value||typeof value!=='object'||depth>4)return;if([].concat(value['@type']||[]).includes('VideoObject'))add(value.name);for(const key of ['@graph','mainEntity','video','itemListElement','item'])for(const item of [].concat(value[key]||[]).slice(0,20))walk(item,depth+1);};
 for(const node of [...document.querySelectorAll('script[type="application/ld+json"]')].slice(0,8))try{if(node.textContent.length<65536)for(const item of [].concat(JSON.parse(node.textContent)))walk(item);}catch{}
 add(document.querySelector('h1')?.textContent);add(document.title);return values.slice(0,20);
}
export function readableVideoTitle(values) {
 return (Array.isArray(values)?values:[]).filter(v=>typeof v==='string').map(v=>v.replace(/\s+/g,' ').trim().slice(0,512)).find(v=>v.length>2&&!/^(?:\d+|[a-f0-9]{16,}|video|player|embed|watch|untitled|detected video)(?:\s*[-|:]\s*\d+)?$/i.test(v))||'';
}
export async function resolveVideoTitle(api,sender,candidates=[]) {
 const local=readableVideoTitle(candidates);if(local)return local;
 try {
  const results=await api.scripting.executeScript({target:{tabId:sender.tab.id,frameIds:[0]},func:pageTitleCandidates});
  const current=await api.tabs.get(sender.tab.id);
  if(current.url!==sender.tab.url||current.incognito)return '';
  return readableVideoTitle([...(results?.[0]?.result||[]),current.title]);
 }catch{return readableVideoTitle([sender.tab?.title]);}
}
