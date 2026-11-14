export type RequestScope = 'anonymous' | 'session' | 'tenant';
export interface RequestSnapshot {
  readonly scope: RequestScope;
  readonly session: number;
  readonly tenantGeneration: number;
  readonly tenantId: string | null;
  readonly signal: AbortSignal;
}
export class StaleContextError extends Error {
  constructor() { super('Kontekst żądania jest już nieaktualny.'); this.name = 'StaleContextError'; }
}

export class RequestContext {
  session = 0;
  tenantGeneration = 0;
  tenantId: string | null = null;
  #sessionAbort = new AbortController();
  #tenantAbort = new AbortController();
  #listeners = new Set<(kind: 'session' | 'tenant') => void>();

  subscribe(listener: (kind: 'session' | 'tenant') => void) { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; }
  newSession() {
    this.#sessionAbort.abort(); this.#tenantAbort.abort();
    this.#sessionAbort = new AbortController(); this.#tenantAbort = new AbortController();
    this.session++; this.tenantGeneration++; this.tenantId = null;
    this.#listeners.forEach(listener => listener('session'));
  }
  selectTenant(id: string | null) {
    this.#tenantAbort.abort(); this.#tenantAbort = new AbortController();
    this.tenantGeneration++; this.tenantId = id;
    this.#listeners.forEach(listener => listener('tenant'));
  }
  capture(scope: RequestScope): RequestSnapshot {
    if (scope === 'tenant' && !this.tenantId) throw new Error('Najpierw wybierz tenanta.');
    return { scope, session: this.session, tenantGeneration: this.tenantGeneration, tenantId: scope === 'tenant' ? this.tenantId : null,
      signal: scope === 'tenant' ? AbortSignal.any([this.#sessionAbort.signal, this.#tenantAbort.signal]) : this.#sessionAbort.signal };
  }
  isCurrent(snapshot: RequestSnapshot) {
    return !snapshot.signal.aborted && snapshot.session === this.session
      && (snapshot.scope !== 'tenant' || snapshot.tenantGeneration === this.tenantGeneration && snapshot.tenantId === this.tenantId);
  }
  assertCurrent(snapshot: RequestSnapshot) { if (!this.isCurrent(snapshot)) throw new StaleContextError(); }
}
