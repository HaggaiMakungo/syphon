import { HOST, validReply } from './handoff.js';
const pending = new WeakMap();

// A setup probe never creates a download or changes the last download receipt.
export function probeDesktop(api, { timeoutMs = 40000 } = {}) {
  if (pending.has(api)) return pending.get(api);
  const operation = (async () => {
    const checkedAt = Date.now();
    const request = { version: 1, action: 'ping', requestId: crypto.randomUUID() };
    let timer, status = 'unavailable';
    try {
      const reply = await Promise.race([
        api.runtime.sendNativeMessage(HOST, request),
        new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Connection timed out')), timeoutMs); })
      ]);
      if (validReply(reply, request) && reply.status === 'ready') status = 'ready';
    } catch { /* Never persist native-host errors, links, or local paths. */ }
    finally { clearTimeout(timer); }
    const result = { checkedAt, status };
    const { desktopConnection } = await api.storage.local.get('desktopConnection');
    if (!desktopConnection || desktopConnection.checkedAt <= checkedAt)
      await api.storage.local.set({ desktopConnection: result });
    return result;
  })();
  pending.set(api, operation);
  operation.finally(() => { if (pending.get(api) === operation) pending.delete(api); }).catch(() => {});
  return operation;
}
export async function handleConnectionInstall(api, details) {
  if (details?.reason === 'install') return probeDesktop(api);
}
