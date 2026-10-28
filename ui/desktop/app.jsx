import React,{useEffect,useRef,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {Manager} from './live-manager.jsx';
import {Floating} from './live-floating.jsx';
import {Tray} from './live-tray.jsx';
import {NewDownload,Confirmation,ConnectionGuide} from './live-dialogs.jsx';
import {send} from './live-components.jsx';

const root=createRoot(document.getElementById('app'));
let model;
window.addEventListener('error',event=>window.__syphonError=String(event.error?.stack||event.message));
window.chrome.webview.addEventListener('message',event=>{
 if(event.data?.type==='typography')return;
 if(event.data?.type==='error'){if(model){model={...model,error:event.data.message};render()}return;}
 if(event.data?.type!=='model')return;
 model=event.data.model;render();
});
function AppView({model}){
 const active=(model.tasks||[]).filter(t=>['Starting','Downloading','Assembling','Publishing'].includes(t.state)).length,completed=(model.tasks||[]).filter(t=>t.state==='Completed').length,previous=useRef(completed),[celebrate,setCelebrate]=useState(false);
 useEffect(()=>{document.body.dataset.activity=active?'active':'idle';},[active]);
 useEffect(()=>{if(completed<=previous.current){previous.current=completed;setCelebrate(false);return;}previous.current=completed;setCelebrate(true);const timer=setTimeout(()=>setCelebrate(false),850);return()=>clearTimeout(timer);},[completed]);
 useEffect(()=>{document.body.dataset.celebrate=String(celebrate);},[celebrate]);
 return model.kind==='floating'?<Floating model={model}/>:model.kind==='dialog'?<NewDownload model={model}/>:model.kind==='confirmation'?<Confirmation model={model}/>:model.kind==='connection'?<ConnectionGuide model={model}/>:model.kind==='tray'?<Tray model={model}/>:<Manager model={model}/>;
}
function render(){document.body.dataset.maximized=String(!!model.isMaximized);document.body.dataset.kind=model.kind;document.body.dataset.page=model.page||'dashboard';root.render(<AppView model={model}/>);}
window.addEventListener('keydown',event=>{if(event.ctrlKey&&event.key.toLowerCase()==='l'){event.preventDefault();if(model?.kind==='main')send('new');else document.getElementById('download-url')?.focus()}});
send('ready');
