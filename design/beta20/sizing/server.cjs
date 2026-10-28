const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = __dirname;
const assets = path.resolve(root, '../../../artifacts/beta20-compact/DesktopAssets');
const types = {'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.png':'image/png','.svg':'image/svg+xml','.woff2':'font/woff2','.ico':'image/x-icon'};
http.createServer((req,res)=>{
 try {
  const url = new URL(req.url, 'http://localhost');
  if(url.pathname==='/favicon.ico'){res.writeHead(204).end();return;}
  const brand = url.pathname.startsWith('/brand/');
  const app = url.pathname.startsWith('/app/') || brand;
  const base = app ? assets : root;
  const relative = decodeURIComponent(brand ? url.pathname.slice(1) : app ? url.pathname.slice(5) : url.pathname.slice(1)) || 'index.html';
  const file = path.resolve(base, relative);
  if (!file.startsWith(base + path.sep)) {res.writeHead(403).end();return;}
  fs.readFile(file,(error,data)=>{
   if(error){res.writeHead(404).end('Not found');return;}
   res.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(data);
  });
 }catch{res.writeHead(400).end('Bad request');}
}).listen(8769,'127.0.0.1');
