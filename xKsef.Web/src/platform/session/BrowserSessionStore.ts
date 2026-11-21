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
  readonly verification: 'idle' | 'checking' | 'error';
  readonly verificationError: string | null;
}
const empty = { user: null, tenants: [], activeTenantId: null, error: null, validation: [], notice: null, verification: 'idle', verificationError: null } as const;

/** All cached identity/tenant data lives here, in memory. No persisted client session. */
export class BrowserSessionStore {
  #listeners = new Set<() => void>();
  #operation = 0;
  #tenantRead = 0;
  #tenantRequest: AbortController | null = null;
  #sessionChanges = new Set<() => void>();
  #verifiedUserId: string | null | undefined;
  #state: SessionState;
  #verification = 0;
  #verificationRequest: AbortController | null = null;
  #restoring = false;
  constructor(readonly context: RequestContext, readonly api: ApiClient) {
    this.#state = { ...empty, status: 'loading', busy: false, recovery: 'restore', ...this.#versions() };
    api.onUnauthorized = () => this.expire();
    api.beforeBusinessRequest = () => {
      if (this.#state.verification !== 'idle') throw new ApiError(0, 'Sesja wymaga potwierdzenia. Ponów weryfikację sesji przed wykonaniem operacji.');
    };
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
  #cancelVerification() { ++this.#verification; this.#verificationRequest?.abort(); this.#verificationRequest = null; }
  #begin(status: SessionState['status']) {
    this.#cancelVerification();
    this.#restoring = false;
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
  async #loadIdentity(operation: number, announceIdentityChange = false, confirmedUser?: BrowserUser) {
    const user = confirmedUser ?? readUser(await this.api.request('/api/browser-auth/me', { scope: 'anonymous' }));
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
    await this.#restore(announceIdentityChange);
  }
  async #restore(announceIdentityChange: boolean, confirmedUser?: BrowserUser) {
    const operation = this.#begin('loading');
    this.#restoring = true;
    try { await this.#loadIdentity(operation, announceIdentityChange, confirmedUser); }
    catch (error) {
      if (error instanceof ApiError && error.status === 401 && this.#current(operation)) { this.#confirmIdentity(null, announceIdentityChange); this.#publish({ status: 'anonymous' }); }
      else this.#failure(operation, error, 'error');
    } finally { if (this.#current(operation)) this.#restoring = false; this.#finish(operation); }
  }
  async verify() {
    if (this.#state.busy) { if (this.#restoring) return this.restore(); return; }
    if (this.#state.status !== 'authenticated' || !this.#state.user) return this.restore();
    this.#cancelVerification(); this.#cancelTenantRead(); ++this.#tenantRead;
    const verification = this.#verification; const operation = this.#operation;
    const controller = new AbortController(); this.#verificationRequest = controller;
    const current = () => this.#current(operation) && verification === this.#verification && !controller.signal.aborted;
    this.#publish({ verification: 'checking', verificationError: null });
    try {
      const user = readUser(await this.api.request('/api/browser-auth/me', { scope: 'anonymous', signal: controller.signal }));
      if (!current()) return;
      if (user.userId !== this.#state.user?.userId) return await this.#restore(true, user);
      // These control reads handle 401 below, after verifying their own operation/abort fence.
      const tenants = readTenants(await this.api.request('/api/tenants', { scope: 'anonymous', signal: controller.signal }));
      if (!current()) return;
      this.#applyTenants(tenants);
      this.#publish({ user, verification: 'idle', verificationError: null });
    } catch (error) {
      if (!current()) return;
      if (error instanceof ApiError && error.status === 401) {
        this.expire(); this.#confirmIdentity(null, true);
      } else this.#publish({ verification: 'error', verificationError: errorMessage(error) });
    } finally {
      if (this.#verificationRequest === controller) this.#verificationRequest = null;
    }
  }
  #applyTenants(tenants: readonly Tenant[]) {
    const old = this.#state.tenants.find(tenant => tenant.id === this.#state.activeTenantId);
    const next = tenants.find(tenant => tenant.id === this.#state.activeTenantId);
    const activeTenantId = next?.id ?? null;
    // The current API exposes membership/activity through this list and effective tenant access through role.
    // Display metadata (including name and list order) is deliberately excluded from the access comparison.
    const accessChanged = activeTenantId !== this.#state.activeTenantId || old?.role !== next?.role;
    if (accessChanged) this.context.selectTenant(activeTenantId);
    this.#publish({ tenants, activeTenantId, error: null, ...this.#versions() });
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
    this.#cancelVerification();
    this.#restoring = false;
    ++this.#operation; ++this.#tenantRead; this.#cancelTenantRead(); this.context.newSession();
    this.#publish({ ...empty, status: 'anonymous', busy: false, notice: 'Sesja wygasła. Zaloguj się ponownie.', ...this.#versions() });
  }
  cancelPending() { this.#cancelVerification(); ++this.#operation; ++this.#tenantRead; this.#cancelTenantRead(); this.context.newSession(); }
  selectTenant(id: string) {
    if (this.#state.status !== 'authenticated' || !this.#state.tenants.some(tenant => tenant.id === id)) return;
    this.context.selectTenant(id);
    this.#publish({ activeTenantId: id, error: null, ...this.#versions() });
  }
  async refreshTenants() {
    if (this.#state.status !== 'authenticated') return;
    if (this.#state.verification !== 'idle') return this.verify();
    const operation = this.#operation; const read = ++this.#tenantRead;
    this.#cancelTenantRead();
    const controller = new AbortController(); this.#tenantRequest = controller;
    try {
      const tenants = readTenants(await this.api.request('/api/tenants', { scope: 'session', signal: controller.signal }));
      if (!this.#current(operation) || read !== this.#tenantRead) return;
      this.#applyTenants(tenants);
    } catch (error) { if (!controller.signal.aborted && this.#current(operation) && read === this.#tenantRead) this.#publish({ error: errorMessage(error) }); }
    finally { if (this.#tenantRequest === controller) this.#tenantRequest = null; }
  }
}
