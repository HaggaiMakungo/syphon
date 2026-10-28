import { filterLinks, submitLinks } from './page-actions.js';
import { handoff } from './handoff.js';
const $ = id => document.getElementById(id);
const key = location.hash.slice(1);
let rows = [], selected = new Set(), busy = false, cancelled = false;
if (/^page-links-[a-f0-9-]{36}$/.test(key)) {
 rows = (await chrome.storage.session.get(key))[key] || [];
 await chrome.storage.session.remove(key);
}
function render() {
 $('links').replaceChildren();
 for (const row of filterLinks(rows.filter(r=>!['accepted','ready','removed','cancelled'].includes(r.status)),$('filter').value)) {
  const label = document.createElement('label');label.className='link';
  const check = document.createElement('input');check.type='checkbox';check.checked=selected.has(row.requestId);check.disabled=busy;
  check.onchange=()=>{check.checked?selected.add(row.requestId):selected.delete(row.requestId);update();};
  const text=document.createElement('span');text.textContent=row.url;label.append(check,text);$('links').append(label);
 }
 update();
}
function update() {$('submit').disabled=busy||!selected.size;$('all').disabled=busy;$('none').disabled=busy;$('filter').disabled=busy;}
$('filter').oninput=render;
$('all').onclick=()=>{for(const row of filterLinks(rows,$('filter').value)) if(!row.status)selected.add(row.requestId);render();};
$('none').onclick=()=>{selected.clear();render();};
$('cancel').onclick=()=>{cancelled=true;if(!busy){rows=[];window.close();}else $('status').textContent='Stopping after the current desktop options close. Cancel the current download in Syphon if needed.';};
$('submit').onclick=async()=>{
 busy=true;cancelled=false;render();$('status').textContent='Confirm each link in Syphon. Cancel stops the remaining links.';
 try {
  const result=await submitLinks(rows.filter(r=>selected.has(r.requestId)),r=>handoff(chrome,r),()=>cancelled);
  for(const row of rows)if(['accepted','ready','removed','cancelled'].includes(row.status)){selected.delete(row.requestId);row.url='';}
  rows=rows.filter(r=>r.url);
  $('status').textContent=`${result.completed} link(s) confirmed. ${result.stopped ? result.message || 'Remaining links were not sent.' : 'Review complete.'}`;
 }catch{ $('status').textContent='Could not send these links. Retry the same selection, or use Retry in the extension popup.'; }
 finally{busy=false;render();}
};
$('status').textContent=rows.length?`${rows.length} links available. Select the links you want.`:'This review expired. Open the page context menu again.';
render();
