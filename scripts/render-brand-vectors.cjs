// Optional asset authoring tool: npm install --prefix .tools/brand-icons @resvg/resvg-js
const {Resvg}=require('../.tools/brand-icons/node_modules/@resvg/resvg-js');
const fs=require('node:fs'), path=require('node:path');
const brand=path.resolve(__dirname,'../design/brand');
fs.writeFileSync(path.join(brand,'syphon_wordmark_clean.png'),new Resvg(fs.readFileSync(path.join(brand,'syphon-wordmark-clean.svg'),'utf8'),{font:{loadSystemFonts:true}}).render().asPng());
