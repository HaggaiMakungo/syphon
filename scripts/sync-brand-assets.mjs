import {copyFile,mkdir} from 'node:fs/promises';
const root=new URL('../',import.meta.url),source=new URL('design/brand/hybrid-v3/',root);
// Restore the approved smooth identity used through Beta 19, preserving its masters.
for(const prefix of ['src/Syphon.Desktop/DesktopAssets/brand/','extensions/syphon/brand/']){
 await mkdir(new URL(prefix,root),{recursive:true});
 for(const size of [16,24,32,48,64,128])await copyFile(new URL('transparent/syphon_emblem_'+size+'.png',source),new URL(prefix+'icon-'+size+'.png',root));
 await copyFile(new URL('transparent/syphon_emblem_128.png',source),new URL(prefix+'emblem.png',root));
 await copyFile(new URL('design/brand/syphon_wordmark_clean.png',root),new URL(prefix+'wordmark.png',root));
}
for(const [from,to] of [['transparent/syphon_emblem_256.png','src/Syphon.Desktop/Assets/Brand/syphon_window_icon.png'],['transparent/syphon_emblem_128.png','src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png'],['windows/syphon_transparent.ico','src/Syphon.Desktop/Assets/Brand/syphon_windows.ico'],['transparent/syphon_emblem_64.png','extensions/syphon/icon.png']])await copyFile(new URL(from,source),new URL(to,root));
