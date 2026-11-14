import { createContext, useContext, useEffect, useState, useSyncExternalStore } from 'react';
import type { ReactNode } from 'react';
import { ApiClient } from '../api/ApiClient';
import { RequestContext } from './RequestContext';
import { BrowserSessionStore } from './BrowserSessionStore';

const SessionContext = createContext<BrowserSessionStore | null>(null);
export function SessionProvider({ children }: { children: ReactNode }) {
  const [store] = useState(() => { const context = new RequestContext(); return new BrowserSessionStore(context, new ApiClient(context)); });
  useEffect(() => { void store.restore(); return () => store.cancelPending(); }, [store]);
  return <SessionContext.Provider value={store}>{children}</SessionContext.Provider>;
}
export function useSession() {
  const store = useContext(SessionContext);
  if (!store) throw new Error('Brak dostawcy sesji.');
  const state = useSyncExternalStore(store.subscribe, store.getSnapshot);
  return { store, state, api: store.api };
}
