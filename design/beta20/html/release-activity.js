// Mirrors DownloadActivityIcon: original smooth artwork, 20 charging frames,
// 50 ms per frame, followed by a single 17-frame completion shimmer.
class SyphonActivityIcon extends HTMLElement {
  connectedCallback(){
    if(this.canvas)return;
    const size=Number(this.getAttribute('size'))||32;
    this.canvas=document.createElement('canvas');
    this.canvas.width=this.canvas.height=size;
    this.style.cssText=`display:inline-block;width:${size}px;height:${size}px;flex-shrink:0`;
    this.canvas.style.cssText='display:block;width:100%;height:100%';
    this.append(this.canvas);this.setAttribute('role','img');
    this.ctx=this.canvas.getContext('2d');
    const image=new Image();
    image.onload=()=>{
      this.ctx.drawImage(image,0,0,size,size);
      this.original=this.ctx.getImageData(0,0,size,size);
      this.setState(this.getAttribute('state')||'idle');
    };
    image.src='brand/syphon-taskbar-logo.png';
  }
  disconnectedCallback(){clearInterval(this.timer)}
  get state(){return this.currentState||'idle'}
  setState(value){
    this.currentState=value;this.setAttribute('aria-label',`Syphon: ${value}`);
    clearInterval(this.timer);this.started=performance.now();this.draw();
    if(!matchMedia('(prefers-reduced-motion: reduce)').matches&&['downloading','complete'].includes(value))this.timer=setInterval(()=>this.draw(),50);
  }
  complete(){this.setState('complete')}
  draw(){
    if(!this.original)return;
    const size=this.canvas.width,elapsed=performance.now()-this.started;
    const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
    const fill=this.state==='downloading'&&!reduced?Math.min(size,Math.round(Math.floor(elapsed%1000/50)/17*size)):size;
    const shimmerFrame=Math.floor(elapsed/50);
    const shine=this.state==='complete'&&!reduced&&shimmerFrame<17?Math.round((shimmerFrame*3-16)*size/32):-99;
    const frame=new ImageData(new Uint8ClampedArray(this.original.data),size,size);
    const pixels=frame.data;
    for(let y=0;y<size;y++)for(let x=0;x<size;x++){
      const at=(y*size+x)*4;if(!pixels[at+3])continue;
      if(this.state==='paused'){
        const gray=Math.round((pixels[at]*.299+pixels[at+1]*.587+pixels[at+2]*.114)*.72);
        pixels[at]=pixels[at+1]=pixels[at+2]=gray;
      }else{
        const factor=y<size-fill?.42:1;
        const highlight=Math.max(pixels[at],pixels[at+1],pixels[at+2])>50?Math.max(0,1-Math.abs(x-y/2-shine)/2.5)*.65:0;
        for(let c=0;c<3;c++)pixels[at+c]=Math.round(pixels[at+c]*factor+(255-pixels[at+c]*factor)*highlight);
      }
    }
    this.ctx.putImageData(frame,0,0);
    if(this.state==='complete'&&shimmerFrame>=17)clearInterval(this.timer);
  }
}
customElements.define('syphon-activity-icon',SyphonActivityIcon);
