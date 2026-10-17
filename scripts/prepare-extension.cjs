const fs = require("node:fs"), path = require("node:path"), crypto = require("node:crypto");
const folder = path.resolve(__dirname, "../extensions/syphon");
const manifestPath = path.join(folder, "manifest.json");
const existing = fs.existsSync(manifestPath) ? JSON.parse(fs.readFileSync(manifestPath, "utf8")) : {};
const key = existing.key ?? crypto.generateKeyPairSync("rsa", { modulusLength: 2048 }).publicKey.export({ type: "spki", format: "der" }).toString("base64");
fs.writeFileSync(manifestPath, JSON.stringify({ ...existing, manifest_version: 3, name: "Syphon this", version: existing.version ?? "0.4.0", description: "Hand a download link to the Syphon desktop app.",
 key, permissions: ["contextMenus", "nativeMessaging", "storage", "notifications"], background: { service_worker: "background.js", type: "module" },
 optional_permissions: existing.optional_permissions ?? ["cookies", "downloads", "scripting", "webRequest"], optional_host_permissions: ["http://*/*", "https://*/*"],
 action: { ...existing.action, default_title: "Syphon this", default_popup: "popup.html", default_icon: Object.fromEntries([16,32,48,64,128].map(size=>[size,`brand/icon-${size}.png`])) }, icons: Object.fromEntries([16,32,48,64,128].map(size=>[size,`brand/icon-${size}.png`])),
 web_accessible_resources: existing.web_accessible_resources ?? [{ resources: ["brand/icon-64.png"], matches: ["http://*/*", "https://*/*"] }],
 content_security_policy: { extension_pages: "script-src 'self'; object-src 'none'" } }, null, 2) + "\n");
// Copy the supplied branding instead of regenerating the retired placeholder icon.
require("node:child_process").execFileSync(process.execPath, [path.join(__dirname, "sync-brand-assets.mjs")], { stdio: "inherit" });
const id = [...crypto.createHash("sha256").update(Buffer.from(key, "base64")).digest().subarray(0,16)].flatMap(b => [b >> 4, b & 15]).map(n => String.fromCharCode(97 + n)).join("");
process.stdout.write(`Extension ID: ${id}\n`);
