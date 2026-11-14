import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../api/ApiClient';
import type { Fetch } from '../api/ApiClient';
import { BrowserSessionStore } from './BrowserSessionStore';
import { RequestContext, StaleContextError } from './RequestContext';
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const tenants = [{ id: 'A', name: 'Same name', role: 'Admin' }, { id: 'B', name: 'Same name', role: 'User' }];
function fixture() {
  const calls: { path: string; init: RequestInit }[] = []; let signedIn = false; let token = 0;
  const controls = { tenantsStatus: 200, logoutStatus: 204, registerStatus: 201, meOverride: null as (() => Promise<Response>) | null };
  const fetcher: Fetch = async (input, init = {}) => {
    const path = String(input); calls.push({ path, init });
    if (path.endsWith('/csrf')) return json({ requestToken: `csrf-${++token}-${signedIn}`, headerName: 'X-CSRF-TOKEN' });
    if (path.endsWith('/me')) return controls.meOverride ? controls.meOverride() : signedIn ? json({ userId: 'user-1', login: 'user' }) : json({}, 401);
    if (path.endsWith('/login')) { signedIn = true; return json({ userId: 'user-1', login: 'user' }); }
    if (path.endsWith('/register')) return json({ userId: 'new-user', errors: { Identity: ['Email already used'] } }, controls.registerStatus);
    if (path.endsWith('/logout')) { if (controls.logoutStatus === 204) { signedIn = false; return new Response(null, { status: 204 }); } return json({}, controls.logoutStatus); }
    if (path === '/api/tenants') return json(tenants, controls.tenantsStatus);
    return json({ value: 'business result' });
  };
  const context = new RequestContext(); const api = new ApiClient(context, fetcher); const store = new BrowserSessionStore(context, api);
  return { store, api, context, calls, controls };
}

describe('browser session', () => {
  it('restores anonymous state without treating expected /me 401 as session expiry', async () => {
    const { store } = fixture(); await store.restore(); expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', notice: null, busy: false, user: null });
  });
  it('logs in, reads /me and tenants, and rotates CSRF for the signed-in identity', async () => {
    const { store, calls, api } = fixture(); await store.restore(); expect(await store.login('user', 'Synthetic-password-1!')).toBe(true);
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, tenants, activeTenantId: 'A', busy: false });
    await api.request('/api/action', { scope: 'tenant', method: 'POST' });
    const login = calls.find(call => call.path.endsWith('/login'))!; expect(new Headers(login.init.headers).get('X-CSRF-TOKEN')).toBe('csrf-1-false');
    const action = calls.at(-1)!; expect(new Headers(action.init.headers).get('X-CSRF-TOKEN')).toBe('csrf-2-true'); expect(new Headers(action.init.headers).get('X-Tenant-Id')).toBe('A');
    calls.filter(call => call.path.includes('browser-auth')).forEach(call => { expect(new Headers(call.init.headers).has('Authorization')).toBe(false); expect(new Headers(call.init.headers).has('X-Tenant-Id')).toBe(false); });
  });
  it('registers without auto login or tenant assignment', async () => {
    const { store, calls } = fixture(); await store.restore(); expect(await store.register('new', 'new@example.invalid', 'Synthetic-password-1!')).toBe(true);
    expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', user: null, tenants: [] }); expect(store.getSnapshot().notice).toContain('Konto');
    expect(calls.some(call => call.path.endsWith('/login') || call.path === '/api/tenants')).toBe(false);
  });
  it('reports Identity validation and does not retry registration', async () => {
    const { store, calls, controls } = fixture(); await store.restore(); controls.registerStatus = 400;
    expect(await store.register('new', 'new@example.invalid', 'weak')).toBe(false); expect(store.getSnapshot().validation).toEqual(['Email already used']);
    expect(calls.filter(call => call.path.endsWith('/register'))).toHaveLength(1);
  });
  it('clears all cached data immediately on logout, then refreshes anonymous CSRF', async () => {
    const { store, calls, context } = fixture(); await store.restore(); await store.login('user', 'password'); const old = context.capture('tenant');
    const logout = store.logout(); expect(store.getSnapshot()).toMatchObject({ user: null, tenants: [], activeTenantId: null, busy: true }); expect(old.signal.aborted).toBe(true);
    await logout; expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', busy: false }); expect(calls.at(-1)?.path).toBe('/api/browser-auth/csrf');
  });
  it('does not claim successful server logout when the store is unavailable', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); controls.logoutStatus = 503;
    await store.logout(); expect(store.getSnapshot()).toMatchObject({ status: 'error', recovery: 'logout', user: null, tenants: [], busy: false });
    expect(store.getSnapshot().error).toContain('Nie udało się potwierdzić wylogowania');
  });
  it('clears identity and tenant context on session expiry', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); controls.tenantsStatus = 401;
    await store.refreshTenants(); expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', user: null, tenants: [], activeTenantId: null, busy: false }); expect(store.getSnapshot().notice).toContain('wygasła');
  });
  it('clears the selected tenant after 403 and refreshes access without business retries', async () => {
    const { store, api } = fixture(); await store.restore(); await store.login('user', 'password');
    api.onTenantForbidden!(); await vi.waitFor(() => expect(store.getSnapshot().activeTenantId).toBeNull()); expect(store.getSnapshot().user).not.toBeNull();
  });
  it('does not allow selecting a tenant absent from the current user list', async () => {
    const { store } = fixture(); await store.restore(); await store.login('user', 'password'); store.selectTenant('foreign'); expect(store.getSnapshot().activeTenantId).toBe('A');
  });
  it('invalidates A → B → A responses despite identical final TenantId', async () => {
    const { store, context } = fixture(); await store.restore(); await store.login('user', 'password'); const first = context.capture('tenant');
    store.selectTenant('B'); store.selectTenant('A'); expect(context.isCurrent(first)).toBe(false); expect(store.getSnapshot().tenantGeneration).toBeGreaterThan(first.tenantGeneration);
  });
  it('does not let an older operation clear loading or overwrite a later restore', async () => {
    const { store, controls } = fixture(); let resolveOld!: (response: Response) => void; let resolveNew!: (response: Response) => void;
    controls.meOverride = () => new Promise(resolve => { resolveOld = resolve; }); const old = store.restore();
    controls.meOverride = () => new Promise(resolve => { resolveNew = resolve; }); const newer = store.restore();
    resolveOld(json({ userId: 'old', login: 'old' })); await old; expect(store.getSnapshot().busy).toBe(true); expect(store.getSnapshot().user).toBeNull();
    resolveNew(json({}, 401)); await newer; expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', busy: false });
  });
  it('ignores delayed identity after session cancellation', async () => {
    const { store, controls } = fixture(); let resolve!: (response: Response) => void;
    controls.meOverride = () => new Promise(done => { resolve = done; }); const pending = store.restore(); store.expire();
    resolve(json({ userId: 'previous-user', login: 'previous' })); await pending; expect(store.getSnapshot()).toMatchObject({ user: null, tenants: [], status: 'anonymous' });
  });
  it('blocks overlapping authentication mutations and repeated logout', async () => {
    const { store, controls, calls } = fixture(); await store.restore(); let resolve!: (response: Response) => void;
    controls.meOverride = () => new Promise(done => { resolve = done; }); const login = store.login('user', 'password');
    await vi.waitFor(() => expect(calls.filter(call => call.path.endsWith('/me')).length).toBe(2));
    expect(await store.register('other', 'other@example.invalid', 'password')).toBe(false); await store.logout();
    resolve(json({ userId: 'user-1', login: 'user' })); await login; expect(calls.some(call => call.path.endsWith('/logout') || call.path.endsWith('/register'))).toBe(false);
  });
  it('exposes the same guarded API instance for future tenant modules', async () => {
    const { store, api, context } = fixture(); expect(store.api).toBe(api); expect(store.context).toBe(context);
    await expect(api.request('/api/business', { scope: 'tenant' })).rejects.not.toBeInstanceOf(StaleContextError);
  });
});
