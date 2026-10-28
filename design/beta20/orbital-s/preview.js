const hero=document.querySelector('#hero-icon'),slider=document.querySelector('#progress'),percentage=document.querySelector('#percentage'),status=document.querySelector('#status');
function show(){percentage.textContent=slider.value+' ms';status.textContent=hero.state==='complete'?(hero.shadowRoot.querySelector('.shimmer.run')?'Complete · one final shimmer':'Complete · settled'):hero.state==='paused'?'Paused · loop held':hero.state==='idle'?'Ready for the next download':'Downloading · fast fill loop';document.querySelector('#pause').textContent=hero.state==='paused'?'Resume':'Pause';}
slider.addEventListener('input',()=>{hero.setAttribute('duration',slider.value);show()});
document.querySelector('#simulate').onclick=()=>{hero.setState('downloading');show()};
document.querySelector('#pause').onclick=()=>{if(hero.state==='complete'||hero.state==='idle')return;hero.setState(hero.state==='paused'?'downloading':'paused');show()};
document.querySelector('#complete').onclick=()=>{hero.complete();show()};
document.querySelector('#reset').onclick=()=>{hero.reset();show()};
hero.addEventListener('completion',()=>show());hero.addEventListener('shimmerend',()=>show());
for(const size of [16,20,24,32,40,48,64,96]){const item=document.createElement('div');item.className='size-sample';const img=document.createElement('img');img.src='png/transparent/orbital-s-'+size+'.png';img.width=size;img.height=size;img.alt='Orbital S at '+size+' pixels';const label=document.createElement('span');label.textContent=size+'px';item.append(img,label);document.querySelector('#sizes').append(item);}
const motion=matchMedia('(prefers-reduced-motion: reduce)');function motionNote(){document.querySelector('#motion-note').textContent=motion.matches?'Reduced motion is enabled: the active icon stays coloured and both loop and shimmer are skipped.':'The loop signals activity. It does not represent the download percentage.'}motion.addEventListener('change',motionNote);motionNote();show();
