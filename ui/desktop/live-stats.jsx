import React,{useEffect,useId,useState} from 'react';
import {Icon,bytes} from './live-components.jsx';
import {downloadStats} from './download-stats.mjs';

export function useFullscreenStats(maximized){
 const [wide,setWide]=useState(()=>matchMedia('(min-width:1050px) and (min-height:650px)').matches);
 useEffect(()=>{const media=matchMedia('(min-width:1050px) and (min-height:650px)'),change=()=>setWide(media.matches);media.addEventListener('change',change);return()=>media.removeEventListener('change',change)},[]);
 return maximized&&wide;
}
function Metric({icon,label,value,format=value=>value.toLocaleString(),note,index}){
 const [progress,setProgress]=useState(()=>matchMedia('(prefers-reduced-motion:reduce)').matches?1:0);
 useEffect(()=>{if(matchMedia('(prefers-reduced-motion:reduce)').matches){setProgress(1);return}let frame,start;const step=now=>{start??=now;const t=Math.min(1,(now-start)/760);setProgress(1-Math.pow(1-t,3));if(t<1)frame=requestAnimationFrame(step)};frame=requestAnimationFrame(step);return()=>cancelAnimationFrame(frame)},[]);
 return <article className="overview-stat" style={{'--stat-index':index}}><span className="overview-stat-icon"><Icon name={icon}/></span><div><span className="overview-stat-label">{label}</span><strong aria-label={format(value)}><span aria-hidden="true">{format(Math.round(value*progress))}</span></strong><small>{note}</small></div></article>;
}
export function FullscreenSummary({model}){
 const s=downloadStats(model.tasks,model.samples);
 return <section className="fullscreen-summary" aria-label="Download overview"><Metric index={0} icon="circle-check" label="Completed" value={s.completed} note="Downloads in your history"/><Metric index={1} icon="folder" label="Completed data" value={s.completedBytes} format={bytes} note="Across completed downloads"/><Metric index={2} icon="download" label="In progress" value={s.active} note={`${s.paused} paused · ${s.queued} waiting`}/></section>;
}
export function FullscreenActivity({model}){
 const s=downloadStats(model.tasks,model.samples),id=useId().replace(/:/g,''),samples=(model.samples||[]).slice(-60).map(n=>Number.isFinite(n)?Math.max(0,n):0),max=Math.max(1,...samples),plot=samples.length>1?samples:[0,0];
 const points=plot.map((v,i)=>[i/(plot.length-1)*320,126-v/max*110]);
 const path=points.map(([x,y],i)=>i?`C ${(points[i-1][0]+x)/2} ${points[i-1][1]} ${(points[i-1][0]+x)/2} ${y} ${x} ${y}`:`M ${x} ${y}`).join(' '),area=path+' L 320 136 L 0 136 Z';
 const labels={Video:'Videos',Audio:'Music',Images:'Images',Documents:'Documents',Archives:'Compressed',Programs:'Programs',Other:'Other files'};
 return <aside className="fullscreen-activity" aria-label="Transfer statistics"><section className="activity-panel"><header><span className="stats-eyebrow">LIVE ACTIVITY</span><span className={'activity-state '+(s.active?'running':'')}>{s.active?'Downloading':'Idle'}</span></header><div className="activity-speed"><strong>{bytes(model.currentRate||0)}<small>/s</small></strong><span>Total download speed</span></div><div className="stats-speed-chart" role="img" aria-label={samples.some(n=>n>0)?'Recent combined download speed':'No recent download activity'}><svg viewBox="0 0 320 140" preserveAspectRatio="none" aria-hidden="true"><defs><linearGradient id={'stats-area-'+id} x1="0" y1="0" x2="0" y2="1"><stop stopColor="#bd86ed" stopOpacity=".28"/><stop offset="1" stopColor="#bd86ed" stopOpacity="0"/></linearGradient><linearGradient id={'stats-line-'+id}><stop stopColor="#8557aa"/><stop offset="1" stopColor="#dfb4ff"/></linearGradient></defs>{[20,73,126].map(y=><line key={y} x1="0" x2="320" y1={y} y2={y} className="stats-chart-grid"/>)}<path d={area} fill={'url(#stats-area-'+id+')'} className="stats-chart-area"/><path d={path} fill="none" stroke={'url(#stats-line-'+id+')'} strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" pathLength="1" className="stats-chart-line"/></svg><div className="stats-chart-caption"><span>Recent activity</span><span>Peak {bytes(s.peak)}/s</span></div></div><div className="activity-queue"><span><i className="queue-state active"/>{s.active} active</span><span><i className="queue-state paused"/>{s.paused} paused</span><span><i className="queue-state waiting"/>{s.queued} waiting</span></div></section><section className="file-mix-panel"><header><span className="stats-eyebrow">YOUR DOWNLOADS</span><Icon name="folder"/></header><h2>By file type</h2>{s.types.length?<div className="file-mix">{s.types.map((type,i)=><div className="file-mix-row" key={type.name} style={{'--mix-index':i}}><div><span>{labels[type.name]||type.name}</span><strong>{type.count}</strong></div><div className="file-mix-track"><i style={{width:type.percent+'%'}}/></div></div>)}</div>:<p className="stats-empty">Your completed downloads will appear here.</p>}<p className="stats-history-note">Based on your saved download history.</p></section></aside>;
}
