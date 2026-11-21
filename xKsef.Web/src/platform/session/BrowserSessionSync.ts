import type { BrowserSessionStore } from './BrowserSessionStore';

export const sessionChannelName = 'xksef.session';
export const sessionChanged = 'session-changed';
export interface SessionChannel {
  onmessage: ((event: MessageEvent) => void) | null;
  postMessage(message: unknown): void;
  close(): void;
}
export interface SessionVisibility extends EventTarget { readonly visibilityState: string }

/** Messages invalidate local state; only /me and /tenants may establish the new identity and permissions. */
export function connectBrowserSession(store: BrowserSessionStore, visibility: SessionVisibility = document,
    createChannel: () => SessionChannel | null = () => typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(sessionChannelName)) {
  let channel: SessionChannel | null = null;
  try { channel = createChannel(); } catch { /* Visibility verification remains available when channels are unsupported. */ }
  let active = true;
  let verification: Promise<void> | null = null;
  let previousVisibility = visibility.visibilityState;
  function verify(remote: boolean, returned = false) {
    if (!active) return;
    // Repeated visible events share one check; an actual return or a new remote signal supersedes it.
    // Do not interrupt a local authentication mutation on visibility alone.
    if (!remote && (verification ? !returned : store.getSnapshot().busy)) return;
    const pending = remote ? store.restore(false) : store.verify();
    verification = pending;
    void pending.finally(() => { if (verification === pending) verification = null; });
  }
  const unsubscribe = store.subscribeSessionChanges(() => {
    try { channel?.postMessage(sessionChanged); } catch { /* Do not turn a channel failure into an authentication failure. */ }
  });
  if (channel) channel.onmessage = event => { if (event.data === sessionChanged) verify(true); };
  const visible = () => {
    const current = visibility.visibilityState; const returned = previousVisibility !== 'visible' && current === 'visible'; previousVisibility = current;
    if (current === 'visible') verify(false, returned);
  };
  visibility.addEventListener('visibilitychange', visible);
  const disconnect = () => {
    active = false; unsubscribe(); visibility.removeEventListener('visibilitychange', visible);
    if (channel) { channel.onmessage = null; channel.close(); }
  };
  return { disconnect, restore: () => active ? store.restore() : Promise.resolve() };
}
