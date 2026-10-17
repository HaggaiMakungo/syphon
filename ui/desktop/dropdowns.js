// Native selects remain hidden state adapters for the existing application actions.
// All visible interaction uses the same accessible, locally rendered component.
const controls = new Map();
let opened = null, sequence = 0;
function close(restore = false) {
  if (!opened) return;
  const c = opened; opened = null;
  c.animation?.cancel(); c.fade?.cancel();
  c.menu.hidePopover(); c.trigger.setAttribute('aria-expanded', 'false');
  c.trigger.removeAttribute('aria-activedescendant');
  if (restore && c.trigger.isConnected) c.trigger.focus();
}
function position(c) {
  const r = c.trigger.getBoundingClientRect(), gap = c.integrated ? 0 : 3.2, margin = 6.4;
  const style = getComputedStyle(c.trigger);
  const font = `400 ${style.fontSize} ${style.fontFamily}`;
  c.menu.style.font = font;
  const measure = document.createElement('canvas').getContext('2d'); measure.font = font;
  const below = innerHeight - r.bottom - gap - margin, above = r.top - gap - margin;
  const needed = Array.from(c.select.options).filter(o=>!o.hidden).length * 28 + 8;
  const up = below < Math.min(240,needed) && above > below;
  // Include the selected-option checkmark without wrapping short option labels.
  const width = Math.min(innerWidth - margin * 2, Math.max(r.width, ...Array.from(c.select.options, o => Math.ceil(measure.measureText(o.text).width) + 64)));
  c.menu.dataset.direction=up?'up':'down';
  Object.assign(c.menu.style, {width: width + 'px', maxHeight: Math.max(28, Math.min(240, up ? above : below) + (c.integrated?r.height:0)) + 'px',
    left: Math.max(margin, Math.min(r.right - width, innerWidth - width - margin)) + 'px', top: 'auto', bottom: 'auto'});
  c.menu.style[up ? 'bottom' : 'top'] = (up ? innerHeight - (c.integrated?r.bottom:r.top) + gap : (c.integrated?r.top:r.bottom) + gap) + 'px';
  if(c.header)c.header.style.height=r.height+'px';
}
function highlight(c, index, active = true) {
  c.active = index;
  for (const row of c.options.children) row.dataset.active = String(active && Number(row.dataset.index) === index);
  const row = c.menu.querySelector(`[data-index="${index}"]`);
  if (row) { c.trigger.setAttribute('aria-activedescendant', row.id); row.scrollIntoView({block: 'nearest'}); }
}
function choose(c, index) {
  if (c.select.disabled || !enabled(c, index)) return;
  const changed = c.select.selectedIndex !== index;
  c.select.selectedIndex = index; close(true); syncDropdowns();
  if (changed) { c.select.dispatchEvent(new Event('input', {bubbles:true})); c.select.dispatchEvent(new Event('change', {bubbles:true})); }
  syncDropdowns();
}
const enabled = (c, i) => !!c.select.options[i] && !c.select.options[i].disabled && !c.select.options[i].hidden && !c.select.options[i].parentElement?.disabled;
function open(c) {
  if (c.select.disabled || c.trigger.closest('[inert]')) return;
  close(); opened = c; c.menu.replaceChildren();
  c.integrated=Array.from(c.select.options).filter(o=>!o.hidden).length<=12;
  c.menu.dataset.integrated=String(c.integrated); c.trigger.dataset.integrated=String(c.integrated);
  c.header=null;
  if(c.integrated){
    c.header=document.createElement('div');c.header.className='syphon-select-header';c.header.setAttribute('role','presentation');c.header.setAttribute('aria-hidden','true');
    const title=document.createElement('span');title.textContent=c.select.selectedOptions[0]?.text||'';
    c.header.append(title);c.header.onpointerdown=e=>e.preventDefault();c.header.onclick=()=>close(true);c.menu.append(c.header);
  }
  c.options=document.createElement('div');c.options.className='syphon-select-options';c.options.setAttribute('role','presentation');c.menu.append(c.options);
  Array.from(c.select.options).forEach((option, index) => {
    if (option.hidden) return;
    const row = document.createElement('div'); row.id = c.menu.id + '-' + index; row.dataset.index = index;
    row.className = 'syphon-select-option'; row.setAttribute('role', 'option');
    row.setAttribute('aria-selected', String(option.selected)); row.setAttribute('aria-disabled', String(!enabled(c, index)));
    const label = document.createElement('span'); label.textContent = option.text;
    const check = document.createElement('span'); check.className = 'syphon-select-check'; check.textContent = option.selected ? '✓' : ''; check.setAttribute('aria-hidden', 'true');
    row.append(label,check); row.onpointermove = () => { if(enabled(c,index)) highlight(c,index); };
    row.onpointerdown = e => e.preventDefault(); row.onclick = e => { e.stopPropagation(); choose(c,index); };
    c.options.append(row);
  });
  c.menu.setAttribute('aria-label', c.trigger.getAttribute('aria-label'));
  c.menu.showPopover(); position(c); c.trigger.setAttribute('aria-expanded','true');
  highlight(c, enabled(c,c.select.selectedIndex) ? c.select.selectedIndex : Array.from(c.select.options).findIndex((_,i)=>enabled(c,i)),c.trigger.matches(':focus-visible'));
  if(c.integrated&&!matchMedia('(prefers-reduced-motion: reduce)').matches){
    const height=c.menu.getBoundingClientRect().height;
    c.animation=c.menu.animate([{height:c.trigger.getBoundingClientRect().height+'px'},{height:height+'px'}],{duration:180,easing:'cubic-bezier(0.2,0,0,1)'});
    c.fade=c.options.animate([{opacity:0},{opacity:1}],{duration:180,easing:'cubic-bezier(0.2,0,0,1)'});
  }
}
function key(c, e) {
  const keys = ['ArrowDown','ArrowUp','Home','End','Enter',' ','Escape','Tab','PageDown','PageUp'];
  if (e.key === 'Tab') { if(opened===c) close(); return; }
  if (e.key === 'Escape') { if(opened===c) { e.preventDefault(); e.stopImmediatePropagation(); close(true); } return; }
  if (keys.includes(e.key)) {
    e.preventDefault(); e.stopPropagation();
    if (e.key === 'Enter' || e.key === ' ') { if(opened===c) choose(c,c.active); else open(c); return; }
    if (opened!==c) open(c);
    const indices = Array.from(c.select.options).map((_,i)=>i).filter(i=>enabled(c,i));
    const at = indices.indexOf(c.active), step = e.key==='ArrowUp' || e.key==='PageUp' ? -1 : 1;
    const next = e.key==='Home' ? 0 : e.key==='End' ? indices.length-1 : Math.max(0,Math.min(indices.length-1,at+step*(e.key.startsWith('Page')?5:1)));
    if(indices.length) highlight(c,indices[next]);
  } else if (e.key.length===1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
    e.preventDefault(); if(opened!==c) open(c);
    const now = performance.now(); c.search = now-(c.searchAt||0)>700 ? e.key : (c.search||'')+e.key; c.searchAt=now;
    const start = c.active+1, options = Array.from(c.select.options);
    for(let n=0;n<options.length;n++) { const i=(start+n)%options.length; if(enabled(c,i)&&options[i].text.toLocaleLowerCase().startsWith(c.search.toLocaleLowerCase())) { highlight(c,i); break; } }
  }
}
export function syncDropdowns() {
  for (const [select,c] of controls) if (!select.isConnected) { if(opened===c) close(); c.trigger.remove(); c.menu.remove(); controls.delete(select); }
  for (const select of document.querySelectorAll('select:not([multiple])')) {
    let c = controls.get(select);
    if (!c) {
      const trigger = document.createElement('button'), label = document.createElement('span'), arrow = document.createElementNS('http://www.w3.org/2000/svg','svg'), menu = document.createElement('div');
      trigger.type='button'; trigger.className='syphon-select-trigger'; trigger.id=(select.id||'syphon-select-'+(++sequence))+'-trigger';
      trigger.setAttribute('role','combobox'); trigger.setAttribute('aria-haspopup','listbox'); trigger.setAttribute('aria-expanded','false'); trigger.setAttribute('aria-autocomplete','none');
      label.className='syphon-select-value'; arrow.setAttribute('class','syphon-select-arrow'); arrow.setAttribute('viewBox','0 0 16 16'); arrow.setAttribute('aria-hidden','true'); arrow.setAttribute('focusable','false'); const path=document.createElementNS('http://www.w3.org/2000/svg','path'); path.setAttribute('d','M4 6l4 4 4-4'); path.setAttribute('fill','none'); path.setAttribute('stroke','currentColor'); path.setAttribute('stroke-width','1.5'); path.setAttribute('stroke-linecap','round'); path.setAttribute('stroke-linejoin','round'); arrow.append(path); trigger.append(label,arrow);
      menu.id='syphon-select-menu-'+(++sequence); menu.className='syphon-select-menu'; menu.setAttribute('role','listbox'); menu.setAttribute('popover','manual');
      trigger.setAttribute('aria-controls',menu.id); select.hidden=true; select.tabIndex=-1; select.setAttribute('aria-hidden','true');
      for(const sibling of select.parentElement.children) if(sibling.matches('svg.icon-tabler-chevron-down')) sibling.style.display='none';
      for(const lab of Array.from(select.labels||[])) lab.htmlFor=trigger.id;
      select.after(trigger); document.body.append(menu); c={select,trigger,label,menu,active:0}; controls.set(select,c);
      trigger.onclick=e=>{e.preventDefault();e.stopPropagation();if(opened===c)close();else open(c);}; trigger.onkeydown=e=>key(c,e);
      select.addEventListener('change',syncDropdowns);
      // A surrounding native label otherwise forwards clicks back to the hidden select.
      trigger.onpointerdown=e=>e.stopPropagation();
    }
    const value=select.selectedOptions[0]?.text||'';
    if(c.label.textContent!==value)c.label.textContent=value;
    c.trigger.disabled=select.disabled;
    c.trigger.setAttribute('aria-label',select.getAttribute('aria-label')||select.labels?.[0]?.textContent.trim()||select.title||'Choose an option');
    c.trigger.title=value;
    if(opened===c) for(const row of c.options.children) {
      const option=select.options[Number(row.dataset.index)];
      row.setAttribute('aria-selected',String(!!option?.selected));
      row.querySelector('.syphon-select-check').textContent=option?.selected?'✓':'';
    }
    if(opened===c && (select.disabled || c.trigger.closest('[inert]')))close();
  }
}
document.addEventListener('pointerdown',e=>{if(opened&&!opened.menu.contains(e.target)&&!opened.trigger.contains(e.target))close();},true);
document.addEventListener('focusin',e=>{if(opened&&!opened.trigger.contains(e.target)&&!opened.menu.contains(e.target))close();});
window.addEventListener('resize',()=>{if(opened)position(opened);});
document.addEventListener('scroll',e=>{if(opened&&!opened.menu.contains(e.target))position(opened);},true);
new MutationObserver(records=>{if(records.some(r=>r.type==='childList'&&Array.from(r.addedNodes).some(n=>n.nodeType===1&&(n.matches('select')||n.querySelector('select')))))syncDropdowns();})
  .observe(document.documentElement,{childList:true,subtree:true});

window.addEventListener('blur',()=>close());
