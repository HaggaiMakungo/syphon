async (page) => {
  const results=[];
  const errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  for(const variant of ['18','19','20','21']) {
    await page.setViewportSize({width:1024,height:1050});
    await page.goto(`http://127.0.0.1:8767/?v=${variant}`);
    await page.evaluate(()=>document.fonts.ready);
    await page.screenshot({path:`design/beta20/html/preview-${variant}.png`,fullPage:true});
    if(await page.locator('.transfer').count()!==3)throw Error(`Initial queue ${variant}`);
    await page.getByRole('button',{name:'Pause blender.msi',exact:true}).click();
    if(await page.getByRole('button',{name:'Resume blender.msi',exact:true}).count()!==1)throw Error(`Pause ${variant}`);
    await page.getByRole('button',{name:'Resume blender.msi',exact:true}).click();
    if(variant==='19')await page.getByRole('button',{name:'Paused',exact:true}).click();
    else if(variant==='18'){await page.getByRole('combobox',{name:'Filter transfers',exact:true}).click();await page.getByRole('option',{name:'Paused',exact:true}).click()}else await page.getByRole('combobox',{name:'Filter transfers'}).selectOption('paused');
    if(await page.locator('.transfer').count()!==1)throw Error(`Filter ${variant}`);
    if(variant==='19')await page.getByRole('button',{name:'All',exact:true}).click();
    else if(variant==='18'){await page.getByRole('combobox',{name:'Filter transfers',exact:true}).click();await page.getByRole('option',{name:'All downloads',exact:true}).click()}else await page.getByRole('combobox',{name:'Filter transfers'}).selectOption('all');
    if(variant==='18'){await page.getByRole('combobox',{name:'Speed limit',exact:true}).click();await page.getByRole('option',{name:'10 MB/s',exact:true}).click()}else await page.getByRole('combobox',{name:'Speed limit',exact:true}).selectOption('10 MB/s');
    await page.getByRole('button',{name:'Settings',exact:true}).click();
    await page.getByLabel('Adaptive violet glow').uncheck();
    await page.getByRole('button',{name:'Downloads',exact:true}).click();
    if(await page.locator('.transfer.selected').count()!==0)throw Error(`Glow ${variant}`);
    await page.getByRole('button',{name:'New download',exact:true}).click();
    await page.getByRole('button',{name:'Add links',exact:true}).click();
    await page.getByLabel('Download link').fill('https://example.com/test.zip');
    await page.getByLabel('File name').fill('<safe>.zip');
    await page.getByRole('button',{name:'Add to preview',exact:true}).click();
    if(await page.locator('.transfer').count()!==4)throw Error(`Add ${variant}`);
    if(await page.locator('.file-name').last().textContent()!=='<safe>.zip')throw Error('Escaping');
    await page.setViewportSize({width:390,height:844});
    await page.goto(`http://127.0.0.1:8767/?v=${variant}`);
    await page.screenshot({path:`design/beta20/html/mobile-${variant}.png`,fullPage:true});
    const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth);
    if(overflow)throw Error(`Horizontal overflow ${variant}`);
    results.push({variant,pause:true,filter:true,settings:true,add:true,mobileOverflow:false});
  }
  if(errors.length)throw Error(errors.join('\n'));
  await page.setViewportSize({width:1024,height:1050});
  await page.goto('http://127.0.0.1:8767/?v=18');
  console.log(JSON.stringify({results,errors}));
}
