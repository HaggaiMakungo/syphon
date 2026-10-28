export function downloadStats(tasks=[],samples=[]) {
 const result={completed:0,completedBytes:0,active:0,paused:0,queued:0,failed:0,types:[],peak:0};const types=new Map();
 for(const task of tasks){
  if(task.state==='Completed'){result.completed++;result.completedBytes+=Number.isFinite(task.received)?Math.max(0,task.received):0;const type=task.fileType||'Other';types.set(type,(types.get(type)||0)+1);}
  else if(['Starting','Downloading','Assembling','Publishing'].includes(task.state))result.active++;
  else if(task.state==='Paused')result.paused++;
  else if(['Queued','RetryWaiting'].includes(task.state))result.queued++;
  else if(['Failed','Cancelled'].includes(task.state))result.failed++;
 }
 result.types=[...types.entries()].sort((a,b)=>b[1]-a[1]).map(([name,count])=>({name,count,percent:result.completed?100*count/result.completed:0}));
 result.peak=Math.max(0,...samples.filter(Number.isFinite).map(n=>Math.max(0,n)));return result;
}
