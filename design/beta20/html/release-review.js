document.querySelectorAll('[data-icon]').forEach(el=>el.innerHTML=icon(el.dataset.icon));
const bars=[22,38,56,35,69,83,58,48,39,54,78,100,77,58,44,67,85,51,39,27,44,61,32,20];
document.querySelectorAll('.histogram,.monitor-bars').forEach(el=>bars.forEach(n=>{const bar=document.createElement('i');bar.style.setProperty('--bar',n+'%');el.append(bar)}));
const tabs=[...document.querySelectorAll('[role=tab]')];
function activate(tab,focus=false){for(const item of tabs){const selected=item===tab;item.setAttribute('aria-selected',String(selected));item.tabIndex=selected?0:-1;document.getElementById(item.getAttribute('aria-controls')).hidden=!selected}if(focus)tab.focus()}
tabs.forEach((tab,index)=>{tab.onclick=()=>activate(tab);tab.onkeydown=event=>{let target;if(event.key==='ArrowDown'||event.key==='ArrowRight')target=tabs[(index+1)%tabs.length];if(event.key==='ArrowUp'||event.key==='ArrowLeft')target=tabs[(index+tabs.length-1)%tabs.length];if(event.key==='Home')target=tabs[0];if(event.key==='End')target=tabs.at(-1);if(target){event.preventDefault();activate(target,true)}}});
const activeIcons=[...document.querySelectorAll('.taskbar-scene syphon-activity-icon')],toggle=document.querySelector('#taskbar-toggle');
toggle.onclick=()=>{const next=activeIcons[0].state==='downloading'?'paused':'downloading';activeIcons.forEach(el=>el.setState(next));toggle.textContent=next==='paused'?'Resume':'Pause'};
document.querySelector('#taskbar-complete').onclick=()=>{activeIcons.forEach(el=>el.complete());toggle.textContent='Replay'};
const dialog=document.querySelector('#download-dialog');
document.querySelectorAll('[data-download]').forEach(button=>button.onclick=()=>dialog.showModal());
document.querySelectorAll('.dialog-close,.dialog-done').forEach(button=>button.onclick=()=>dialog.close());
dialog.addEventListener('click',event=>{if(event.target===dialog){const bounds=dialog.getBoundingClientRect();if(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom)dialog.close()}});

