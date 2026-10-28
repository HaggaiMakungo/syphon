/* A dropdown unfolds from its trigger as one continuous material surface. */
function refineControls(){
  document.querySelectorAll('.v18 select').forEach((select,index)=>{
    const label=select.getAttribute('aria-label')||(select.id==='motion'?'Motion':'Choose option');
    const box=document.createElement('span');box.className='joined-dropdown';
    const upward=!!select.closest('.footer');box.dataset.direction=upward?'up':'down';
    const trigger=document.createElement('button');trigger.type='button';trigger.className='dropdown-trigger';
    trigger.setAttribute('role','combobox');trigger.setAttribute('aria-label',label);trigger.setAttribute('aria-haspopup','listbox');trigger.setAttribute('aria-expanded','false');
    const list=document.createElement('span');list.className='dropdown-options';list.id=`soft-options-${index}`;list.setAttribute('role','listbox');list.setAttribute('aria-label',label+' options');list.hidden=true;
    trigger.setAttribute('aria-controls',list.id);
    select.before(box);box.append(trigger,list,select);
    select.classList.add('native-select-proxy');select.setAttribute('aria-hidden','true');select.tabIndex=-1;
    const caption=document.createElement('span');caption.textContent=select.selectedOptions[0].textContent;
    trigger.append(caption);const chevron=document.createElement('span');chevron.className='dropdown-chevron';chevron.setAttribute('aria-hidden','true');trigger.append(chevron);
    const close=(restore=false)=>{box.classList.remove('open');trigger.setAttribute('aria-expanded','false');list.hidden=true;if(restore)trigger.focus()};
    const options=[...select.options].map(option=>{
      const item=document.createElement('button');item.type='button';item.className='dropdown-option';item.setAttribute('role','option');item.setAttribute('aria-selected',String(option.selected));item.dataset.value=option.value;item.textContent=option.textContent;
      item.onclick=()=>{select.value=option.value;caption.textContent=option.textContent;options.forEach(x=>x.setAttribute('aria-selected',String(x===item)));close(true);select.dispatchEvent(new Event('change',{bubbles:true}));if(!document.contains(trigger)){const replacement=[...document.querySelectorAll('.dropdown-trigger')].find(x=>x.getAttribute('aria-label')===label);replacement?.focus()}};
      list.append(item);return item;
    });
    const open=()=>{document.querySelectorAll('.joined-dropdown.open').forEach(x=>x._close?.());
      const scrollBody=box.closest('.dialog-body');
      if(scrollBody&&getComputedStyle(scrollBody).overflowY==='auto'){
        const bounds=box.getBoundingClientRect(),region=scrollBody.getBoundingClientRect();
        const below=region.bottom-bounds.bottom,above=bounds.top-region.top,needed=options.length*40+14;
        box.dataset.direction=below<needed&&above>below?'up':'down';
      }
      box.classList.add('open');trigger.setAttribute('aria-expanded','true');list.hidden=false;(options.find(x=>x.getAttribute('aria-selected')==='true')||options[0]).focus()};
    box._close=close;
    trigger.onclick=()=>box.classList.contains('open')?close():open();
    trigger.onkeydown=e=>{if(['ArrowDown','ArrowUp','Enter',' '].includes(e.key)){e.preventDefault();open()}};
    list.onkeydown=e=>{const current=options.indexOf(document.activeElement);if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close(true)}else if(e.key==='Tab'){close()}else if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();const next=e.key==='Home'?0:e.key==='End'?options.length-1:(current+(e.key==='ArrowDown'?1:-1)+options.length)%options.length;options[next].focus()}else if(e.key.length===1&&!e.ctrlKey&&!e.metaKey){const next=options.find(x=>x.textContent.toLowerCase().startsWith(e.key.toLowerCase()));next?.focus()}};
  });
  document.querySelectorAll('.v18 .transfer').forEach(row=>{row.onkeydown=e=>{if(e.target!==row)return;if(e.key==='Enter'||e.key===' '){e.preventDefault();const id=row.dataset.id;row.click();document.querySelector(`.transfer[data-id="${id}"]`)?.focus()}}});
}
document.addEventListener('pointerdown',e=>document.querySelectorAll('.joined-dropdown.open').forEach(box=>{if(!box.contains(e.target))box._close?.()}));
document.addEventListener('focusin',e=>document.querySelectorAll('.joined-dropdown.open').forEach(box=>{if(!box.contains(e.target))box._close?.()}));
