/* Public component: <syphon-orbital-icon progress="34" state="downloading">. */
(()=>{
 let nextId=0;
 class OrbitalIcon extends HTMLElement{
  static observedAttributes=['progress','state','size','label','tile','duration'];
  constructor(){super();this.attachShadow({mode:'open'});this._lastState='idle';this._grid=0;this._motion=matchMedia('(prefers-reduced-motion: reduce)');this._motionChange=()=>{if(this._motion.matches)this._stopShimmer()};}
  connectedCallback(){this._motion.addEventListener('change',this._motionChange);this._render();}
  disconnectedCallback(){this._motion.removeEventListener('change',this._motionChange);this._stopShimmer();}
  attributeChangedCallback(){if(this.isConnected)this._render();}
  get progress(){const value=Number(this.getAttribute('progress'));return Number.isFinite(value)?Math.min(100,Math.max(0,value)):0;}
  set progress(value){this.setProgress(value);}
  get state(){const value=this.getAttribute('state');return ['idle','downloading','paused','complete'].includes(value)?value:'idle';}
  setProgress(value){const n=Number(value);this.setAttribute('progress',String(Number.isFinite(n)?Math.min(100,Math.max(0,n)):0));}
  setState(value){this.setAttribute('state',['idle','downloading','paused','complete'].includes(value)?value:'idle');}
  complete(){this.setProgress(100);this.setState('complete');}
  reset(){this.setState('idle');this.setProgress(0);}
  _stopShimmer(){this.shadowRoot.querySelector('.shimmer')?.classList.remove('run');}
  _render(){
   const size=Math.max(8,Math.min(1024,Number(this.getAttribute('size'))||64));
   const grid=size<=16?16:size<=20?20:size<=24?24:32;
   const shapes=ORBITAL_SHAPES[grid],tile=this.hasAttribute('tile'),pad=tile?Math.round(grid*.125):0,total=grid+pad*2;
   const key=grid+':'+tile;
   if(this._grid!==key){
    this._grid=key;const id='orbital-'+(++nextId);
    this.shadowRoot.innerHTML=`<style>:host{display:inline-block;width:${size}px;height:${size}px;vertical-align:middle;contain:content}svg{display:block;width:100%;height:100%;overflow:visible}.fill-region{transition:none}.fill-region.charging{animation:charging-fill var(--syphon-loop-duration,1000ms) steps(${grid}) infinite;transform-box:view-box;transform-origin:0 0}@keyframes charging-fill{0%{transform:translateY(${grid}px)}85%,100%{transform:translateY(0px)}}:host([state=paused]) .fill-region.charging{animation-play-state:paused}.shimmer{opacity:0;transform:translateX(-${grid*1.5}px)}.shimmer.run{animation:completion-shimmer 850ms steps(24) 1 both}@keyframes completion-shimmer{0%{opacity:0;transform:translateX(-${grid}px)}15%{opacity:.8}75%{opacity:.65}100%{opacity:0;transform:translateX(${grid*1.5}px)}}:host([state=paused]) .filled{opacity:.48}:host([state=idle]) .inactive{opacity:.75}@media(prefers-reduced-motion:reduce){.fill-region.charging{animation:none;transform:none}.shimmer.run{animation:none;opacity:0}}</style><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${total} ${total}" shape-rendering="crispEdges" aria-hidden="true">${tile?`<rect x=".5" y=".5" width="${total-1}" height="${total-1}" rx="${Math.round(total*.22)}" fill="#17171b" stroke="#38343e" shape-rendering="geometricPrecision"/>`:''}<defs><clipPath id="${id}-progress"><rect class="fill-region" x="0" y="${grid}" width="${grid}" height="0"/></clipPath><clipPath id="${id}-mark">${shapes.mask}</clipPath></defs><g transform="translate(${pad} ${pad})">${shapes.globe}<g class="inactive">${shapes.inactive}</g><g clip-path="url(#${id}-progress)" class="filled">${shapes.mark}</g><g clip-path="url(#${id}-mark)"><g class="shimmer"><path d="M-7 -10H-2L${grid+4} ${grid+10}H${grid-1}Z" fill="#fff1ff"/><path d="M-9 -10H-7L${grid-1} ${grid+10}H${grid-3}Z" fill="#ecd3ff" opacity=".45"/></g></g></g></svg>`;
    this.shadowRoot.querySelector('.shimmer').addEventListener('animationend',()=>{this._stopShimmer();this.dispatchEvent(new CustomEvent('shimmerend',{bubbles:true}));});
   }
   this.style.width=size+'px';this.style.height=size+'px';const duration=Math.max(300,Math.min(2000,Number(this.getAttribute('duration'))||1000));this.style.setProperty('--syphon-loop-duration',duration+'ms');
   const state=this.state,percentage=state==='complete'?100:state==='idle'?0:this.progress,filled=Math.round(grid*percentage/100);
   const clip=this.shadowRoot.querySelector('.fill-region');const charging=state==='downloading'||(state==='paused'&&clip.classList.contains('charging'));clip.classList.toggle('charging',charging);const height=charging?grid:filled,y=charging?0:grid-filled;clip.style.y=y+'px';clip.style.height=height+'px';clip.setAttribute('y',String(y));clip.setAttribute('height',String(height));
   this.setAttribute('role','img');this.setAttribute('aria-label',this.getAttribute('label')||('Syphon · '+(state==='idle'?'idle':state==='complete'?'complete':state==='paused'?'paused':'downloading')));
   if(this._initialized&&state==='complete'&&this._lastState!=='complete'){
    if(!this._motion.matches){const shimmer=this.shadowRoot.querySelector('.shimmer');shimmer.classList.remove('run');void shimmer.getBoundingClientRect();shimmer.classList.add('run');}
    this.dispatchEvent(new CustomEvent('completion',{bubbles:true,detail:{animated:!this._motion.matches}}));
   }
   else if(state!=='complete')this._stopShimmer();
   this._lastState=state;this._initialized=true;
  }
 }
 if(!customElements.get('syphon-orbital-icon'))customElements.define('syphon-orbital-icon',OrbitalIcon);
})();
