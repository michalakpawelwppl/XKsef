import { ApiClient } from '../api/ApiClient';
import { ApiError, errorMessage } from '../api/ApiError';
import { RequestContext } from './RequestContext';
import { readTenants, readUser } from './contracts';
import type { BrowserUser, Tenant } from './contracts';

export interface SessionState {
  readonly status: 'loading' | 'anonymous' | 'authenticated' | 'error';
  readonly user: BrowserUser | null;
  readonly tenants: readonly Tenant[];
  readonly activeTenantId: string | null;
  readonly sessionGeneration: number;
  readonly tenantGeneration: number;
  readonly busy: boolean;
  readonly error: string | null;
  readonly validation: readonly string[];
  readonly notice: string | null;
  readonly recovery: 'restore' | 'logout';
}
const empty = { user: null, tenants: [], activeTenantId: null, error: null, validation: [], notice: null } as const;

/** All cached identity/tenant data lives here, in memory. No persisted client session. */
export class BrowserSessionStore {
  #listeners = new Set<() => void>();
  #operation = 0;
  #tenantRead = 0;
  #tenantRequest: AbortController | null = null;
  #sessionChanges = new Set<() => void>();
  #verifiedUserId: string | null | undefined;
  #state: SessionState;
  constructor(readonly context: RequestContext, readonly api: ApiClient) {
    this.#state = { ...empty, status: 'loading', busy: false, recovery: 'restore', ...this.#versions() };
    api.onUnauthorized = () => this.expire();
    api.onTenantForbidden = () => {
      if (this.#state.status !== 'authenticated') return;
      context.selectTenant(null);
      this.#publish({ activeTenantId: null, notice: 'Brak uprawnień do wybranego tenanta. Odświeżono dostęp.', ...this.#versions() });
      void this.refreshTenants();
    };
  }
  getSnapshot = () => this.#state;
  subscribe = (listener: () => void) => { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; };
  subscribeSessionChanges(listener: () => void) { this.#sessionChanges.add(listener); return () => { this.#sessionChanges.delete(listener); }; }
  #announceSessionChange() { this.#sessionChanges.forEach(listener => listener()); }
  #confirmIdentity(userId: string | null, announce: boolean) {
    const previous = this.#verifiedUserId; this.#verifiedUserId = userId;
    if (announce && previous !== undefined && previous !== userId) this.#announceSessionChange();
  }
  #versions() { return { sessionGeneration: this.context.session, tenantGeneration: this.context.tenantGeneration }; }
  #publish(patch: Partial<SessionState>) { this.#state = { ...this.#state, ...patch }; this.#listeners.forEach(listener => listener()); }
  #cancelTenantRead() { this.#tenantRequest?.abort(); this.#tenantRequest = null; }
  #begin(status: SessionState['status']) {
    const operation = ++this.#operation;
    this.#tenantRead++; this.#cancelTenantRead(); this.context.newSession();
    this.#publish({ ...empty, status, busy: true, recovery: 'restore', ...this.#versions() });
    return operation;
  }
  #current(operation: number) { return operation === this.#operation; }
  #failure(operation: number, error: unknown, status: SessionState['status'], recovery: SessionState['recovery'] = 'restore', prefix = '') {
    if (!this.#current(operation)) return;
    this.#publish({ status, recovery, error: prefix + errorMessage(error), validation: error instanceof ApiError ? error.validation : [] });
  }
  #finish(operation: number) { if (this.#current(operation)) this.#publish({ busy: false }); }
  async #loadIdentity(operation: number, announceIdentityChange = false) {
    const user = readUser(await this.api.request('/api/browser-auth/me', { scope: 'anonymous' }));
    if (!this.#current(operation)) return;
    this.#confirmIdentity(user.userId, announceIdentityChange);
    await this.api.refreshCsrf();
    if (!this.#current(operation)) return;
    const tenants = readTenants(await this.api.request('/api/tenants', { scope: 'session' }));
    if (!this.#current(operation)) return;
    const activeTenantId = tenants[0]?.id ?? null;
    this.context.selectTenant(activeTenantId);
    this.#publish({ status: 'authenticated', user, tenants, activeTenantId, error: null, validation: [], ...this.#versions() });
  }
  async restore(announceIdentityChange = true) {
    const operation = this.#begin('loading');
    try { await this.#loadIdentity(operation, announceIdentityChange); }
    catch (error) {
      if (error instanceof ApiError && error.status === 401 && this.#current(operation)) { this.#confirmIdentity(null, announceIdentityChange); this.#publish({ status: 'anonymous' }); }
      else this.#failure(operation, error, 'error');
    } finally { this.#finish(operation); }
  }
  async login(login: string, password: string) {
    if (this.#state.busy) return false;
    const operation = this.#begin('anonymous'); let signedIn = false;
    try {
      await this.api.request('/api/browser-auth/login', { scope: 'anonymous', method: 'POST', body: { login, password } });
      signedIn = true;
      if (this.#current(operation)) this.#announceSessionChange();
      // Read the identity from /me; never treat a login response as a JWT.
      await this.#loadIdentity(operation);
      return this.#current(operation) && this.#state.status === 'authenticated';
    } catch (error) {
      if (!signedIn && error instanceof ApiError && error.status === 401) error = new ApiError(401, 'Nieprawidłowy login lub hasło.');
      this.#failure(operation, error, signedIn ? 'error' : 'anonymous', 'restore', signedIn ? 'Zalogowano, ale nie udało się wczytać sesji. ' : '');
      return false;
    } finally { this.#finish(operation); }
  }
  async register(login: string, email: string, password: string) {
    if (this.#state.busy) return false;
    const operation = this.#begin('anonymous');
    try {
      await this.api.request('/api/browser-auth/register', { scope: 'anonymous', method: 'POST', body: { login, email, password } });
      if (!this.#current(operation)) return false;
      this.#publish({ status: 'anonymous', notice: 'Konto zostało utworzone. Możesz się teraz zalogować.' });
      return true;
    } catch (error) { this.#failure(operation, error, 'anonymous'); return false; }
    finally { this.#finish(operation); }
  }
  async logout() {
    if (this.#state.busy) return;
    const operation = this.#begin('loading'); let signedOut = false;
    try {
      try { await this.api.request('/api/browser-auth/logout', { scope: 'anonymous', method: 'POST' }); }
      catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error; }
      signedOut = true;
      if (!this.#current(operation)) return;
      this.#verifiedUserId = null; this.#announceSessionChange();
      await this.api.refreshCsrf();
      if (!this.#current(operation)) return;
      if (this.#current(operation)) this.#publish({ status: 'anonymous', notice: 'Wylogowano.' });
    } catch (error) {
      this.#failure(operation, error, signedOut ? 'anonymous' : 'error', signedOut ? 'restore' : 'logout',
        signedOut ? 'Wylogowano, ale nie udało się odświeżyć zabezpieczenia formularza. ' : 'Nie udało się potwierdzić wylogowania na serwerze. ');
    } finally { this.#finish(operation); }
  }
  expire() {
    ++this.#operation; ++this.#tenantRead; this.#cancelTenantRead(); this.context.newSession();
    this.#publish({ ...empty, status: 'anonymous', busy: false, notice: 'Sesja wygasła. Zaloguj się ponownie.', ...this.#versions() });
  }
  cancelPending() { ++this.#operation; ++this.#tenantRead; this.#cancelTenantRead(); this.context.newSession(); }
  selectTenant(id: string) {
    if (this.#state.status !== 'authenticated' || !this.#state.tenants.some(tenant => tenant.id === id)) return;
    this.context.selectTenant(id);
    this.#publish({ activeTenantId: id, error: null, ...this.#versions() });
  }
  async refreshTenants() {
    if (this.#state.status !== 'authenticated') return;
    const operation = this.#operation; const read = ++this.#tenantRead;
    this.#cancelTenantRead();
    const controller = new AbortController(); this.#tenantRequest = controller;
    try {
      const tenants = readTenants(await this.api.request('/api/tenants', { scope: 'session', signal: controller.signal }));
      if (!this.#current(operation) || read !== this.#tenantRead) return;
      const activeTenantId = tenants.some(tenant => tenant.id === this.#state.activeTenantId) ? this.#state.activeTenantId : null;
      if (activeTenantId !== this.#state.activeTenantId) this.context.selectTenant(activeTenantId);
      this.#publish({ tenants, activeTenantId, error: null, ...this.#versions() });
    } catch (error) { if (!controller.signal.aborted && this.#current(operation) && read === this.#tenantRead) this.#publish({ error: errorMessage(error) }); }
    finally { if (this.#tenantRequest === controller) this.#tenantRequest = null; }
  }
}
