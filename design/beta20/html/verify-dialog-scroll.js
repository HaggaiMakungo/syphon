async(page)=>{
 const results=[];
 for(const size of [{width:1100,height:1000},{width:360,height:800}]){
  await page.setViewportSize(size);
  for(const view of ['details','speed']){
   await page.goto('http://127.0.0.1:8767/library.html?view='+view);
   const body=page.getByRole('region',{name:(view==='details'?'Download details':'Download speed limit')+' content'});
   const chrome=page.locator('.dialog-chrome'),footer=page.locator('.dialog-actions');
   const before={chrome:await chrome.boundingBox(),footer:await footer.boundingBox()};
   const frame=await page.locator('.dialog-window').boundingBox();
   if(Math.abs(frame.y+frame.height-(before.footer.y+before.footer.height))>2)throw Error('Outer frame protrudes below actions '+view);
   const geometry=await body.evaluate(el=>({height:el.clientHeight,scrollHeight:el.scrollHeight,overflow:getComputedStyle(el).overflowY}));
   if(geometry.scrollHeight<=geometry.height||geometry.overflow!=='auto')throw Error('Missing internal scroll '+view);
   await body.evaluate(el=>el.scrollTop=el.scrollHeight);
   const after={chrome:await chrome.boundingBox(),footer:await footer.boundingBox()};
   if(Math.abs(before.chrome.y-after.chrome.y)>1||Math.abs(before.footer.y-after.footer.y)>1)throw Error('Chrome/actions move '+view);
   if(after.footer.y+after.footer.height>size.height)throw Error('Footer outside viewport '+view);
   if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth))throw Error('Horizontal overflow');
   await page.screenshot({path:`design/beta20/html/scroll-${view}-${size.width}.png`,fullPage:true});
   if(view==='speed'){
    await page.getByRole('combobox',{name:'Speed preset',exact:true}).click();
    await page.getByRole('option',{name:'10 MB/s',exact:true}).click();
    if(await page.getByRole('spinbutton',{name:'Download speed in KB per second',exact:true}).inputValue()!=='10240')throw Error('Joined preset inside scroll');
    await page.getByRole('button',{name:'Apply limit',exact:true}).click();
    if(await page.getByRole('heading',{name:'Speed limit ready',exact:true}).count()!==1)throw Error('Apply limit');
   }
   results.push({view,width:size.width,internalScroll:true,fixedChrome:true,fixedActions:true});
  }
 }
 await page.setViewportSize({width:1100,height:1000});
 await page.goto('http://127.0.0.1:8767/library.html?view=details&id=coast');
 return results;
}
