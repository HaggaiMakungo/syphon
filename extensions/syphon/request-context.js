export const SIGNIN_PERMISSION = { permissions: ["cookies"], origins: ["http://*/*", "https://*/*"] };

// Read only cookies matching this user-initiated file URL, immediately before handoff.
// The result must never be written to extension storage.
export async function withBrowserContext(api, request) {
  const clean = { ...request }; delete clean.context;
  if (clean.action !== "download" || clean.format || /(^|\.)youtube\.com$|(^|\.)youtu\.be$/i.test(new URL(clean.url).hostname)) return clean;
  const { useBrowserSignin } = await api.storage.local.get("useBrowserSignin");
  if (!useBrowserSignin) return clean;
  if (!await api.permissions.contains(SIGNIN_PERMISSION)) throw new Error("Enable browser sign-in permission in the extension.");
  const all = await api.cookies.getAll({ url: clean.url });
  const cookies = all.filter(c => !c.partitionKey).map(c => ({ name: c.name, value: c.value, domain: c.domain,
    path: c.path, secure: c.secure, hostOnly: c.hostOnly, ...(c.expirationDate == null ? {} : { expires: c.expirationDate }) }));
  if (cookies.length > 64 || cookies.reduce((n,c)=>n+c.name.length+c.value.length+c.domain.length+c.path.length,0) > 6000)
    throw new Error("This site's sign-in data is too large to hand off.");
  let referrer;
  if (clean.sourcePage) {
    try {
      const page = new URL(clean.sourcePage), target = new URL(clean.url);
      if (["http:","https:"].includes(page.protocol) && !page.username && !page.password && !(page.protocol === "https:" && target.protocol === "http:")) {
        page.hash = ""; referrer = page.origin === target.origin ? page.href : page.origin + "/";
      }
    } catch {}
  }
  const wire = { ...clean, context: { url: clean.url, cookies, ...(referrer ? { referrer } : {}),
    ...(globalThis.navigator?.userAgent ? { userAgent: navigator.userAgent.slice(0,512) } : {}) } };
  delete wire.sourcePage;
  if (new TextEncoder().encode(JSON.stringify(wire)).length > 16384) throw new Error("Browser download context exceeds the handoff size limit.");
  return wire;
}
