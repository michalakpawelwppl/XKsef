import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../api/ApiClient';
import type { Fetch } from '../api/ApiClient';
import { BrowserSessionStore } from './BrowserSessionStore';
import { RequestContext, StaleContextError } from './RequestContext';
import { connectBrowserSession, sessionChanged } from './BrowserSessionSync';
import type { SessionChannel } from './BrowserSessionSync';
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const tenants = [{ id: 'A', name: 'Same name', role: 'Admin' }, { id: 'B', name: 'Same name', role: 'User' }];
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
class TestVisibility extends EventTarget {
  visibilityState = 'hidden';
  show() { this.visibilityState = 'visible'; this.dispatchEvent(new Event('visibilitychange')); }
}
function synchronization(store: BrowserSessionStore) {
  const visibility = new TestVisibility();
  const channel: SessionChannel = { onmessage: null, postMessage: vi.fn(), close: vi.fn() };
  const { disconnect } = connectBrowserSession(store, visibility, () => channel);
  return { visibility, channel, disconnect, receive: (data: unknown = sessionChanged) => channel.onmessage?.(new MessageEvent('message', { data })) };
}
function fixture() {
  const calls: { path: string; init: RequestInit }[] = []; let signedIn = false; let token = 0;
  const controls = { tenantsStatus: 200, logoutStatus: 204, registerStatus: 201, meOverride: null as (() => Promise<Response>) | null,
    tenantsOverride: null as (() => Promise<Response>) | null, csrfOverride: null as (() => Promise<Response>) | null, businessStatus: 200,
    businessOverride: null as (() => Promise<Response>) | null };
  const fetcher: Fetch = async (input, init = {}) => {
    const path = String(input); calls.push({ path, init });
    if (path.endsWith('/csrf')) return controls.csrfOverride ? controls.csrfOverride() : json({ requestToken: `csrf-${++token}-${signedIn}`, headerName: 'X-CSRF-TOKEN' });
    if (path.endsWith('/me')) return controls.meOverride ? controls.meOverride() : signedIn ? json({ userId: 'user-1', login: 'user' }) : json({}, 401);
    if (path.endsWith('/login')) { signedIn = true; return json({ userId: 'user-1', login: 'user' }); }
    if (path.endsWith('/register')) return json({ userId: 'new-user', errors: { Identity: ['Email already used'] } }, controls.registerStatus);
    if (path.endsWith('/logout')) { if (controls.logoutStatus === 204) { signedIn = false; return new Response(null, { status: 204 }); } return json({}, controls.logoutStatus); }
    if (path === '/api/tenants') return controls.tenantsOverride ? controls.tenantsOverride() : json(tenants, controls.tenantsStatus);
    return controls.businessOverride ? controls.businessOverride() : json({ value: 'business result' }, controls.businessStatus);
  };
  const context = new RequestContext(); const api = new ApiClient(context, fetcher); const store = new BrowserSessionStore(context, api);
  return { store, api, context, calls, controls };
}

describe('cross-tab session verification', () => {
  it('announces confirmed login and logout using only a constant signal, including when post-login session loading fails', async () => {
    const { store, controls } = fixture(); const sync = synchronization(store);
    try {
      await store.restore(); expect(sync.channel.postMessage).not.toHaveBeenCalled();
      controls.tenantsStatus = 503; expect(await store.login('user', 'private-password')).toBe(false);
      expect(sync.channel.postMessage).toHaveBeenCalledExactlyOnceWith(sessionChanged);
      controls.tenantsStatus = 200; await store.restore(); await store.logout();
      expect(vi.mocked(sync.channel.postMessage).mock.calls).toEqual([[sessionChanged], [sessionChanged]]);
    } finally { sync.disconnect(); }
  });
  it('ignores unknown messages and never trusts identity or credentials supplied by a message', async () => {
    const { store, calls } = fixture(); await store.restore(); const sync = synchronization(store);
    try {
      const current = store.getSnapshot(); const count = calls.length;
      sync.receive({ type: sessionChanged, userId: 'forged', token: 'secret' }); sync.receive('other-message'); sync.receive(null);
      expect(store.getSnapshot()).toBe(current); expect(calls).toHaveLength(count); expect(sync.channel.postMessage).not.toHaveBeenCalled();
    } finally { sync.disconnect(); }
  });
  it('does not broadcast an unconfirmed logout and announces the successful explicit retry', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    try {
      controls.logoutStatus = 503; await store.logout();
      expect(store.getSnapshot()).toMatchObject({ status: 'error', recovery: 'logout' }); expect(sync.channel.postMessage).not.toHaveBeenCalled();
      controls.logoutStatus = 204; await store.logout();
      expect(store.getSnapshot().status).toBe('anonymous'); expect(sync.channel.postMessage).toHaveBeenCalledExactlyOnceWith(sessionChanged);
    } finally { sync.disconnect(); }
  });
  it('keeps data hidden until current tenant permissions are confirmed even when the user identity is unchanged', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    const entered = deferred<void>(); const permissions = deferred<Response>();
    controls.tenantsOverride = () => { entered.resolve(); return permissions.promise; };
    try {
      sync.visibility.show(); await entered.promise;
      expect(store.getSnapshot()).toMatchObject({ status: 'loading', user: null, tenants: [], activeTenantId: null });
      permissions.resolve(json([]));
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, tenants: [], activeTenantId: null }));
      expect(sync.channel.postMessage).not.toHaveBeenCalled();
    } finally { sync.disconnect(); }
  });
  it.each([200, 401, 403, 503].flatMap(status => [{ status, decoding: false }, { status, decoding: true }]))(
    'invalidates old HTTP $status on a remote signal, including during JSON decoding ($decoding)', async ({ status, decoding }) => {
      const { store, controls, api, context } = fixture(); await store.restore(); await store.login('user', 'password');
      const sync = synchronization(store); const responseGate = deferred<Response>(); const bodyGate = deferred<unknown>(); const entered = deferred<void>();
      const oldResponse = json({}, status);
      oldResponse.json = () => { entered.resolve(); return bodyGate.promise; };
      controls.businessOverride = () => decoding ? Promise.resolve(oldResponse) : responseGate.promise;
      const oldContext = context.capture('tenant');
      const old = api.request('/api/old-invoices', { scope: 'tenant' }); const rejected = expect(old).rejects.toBeInstanceOf(StaleContextError);
      if (decoding) await entered.promise;
      const identity = deferred<Response>(); controls.meOverride = () => identity.promise;
      controls.tenantsOverride = async () => json([tenants[1]]);
      const snapshots: ReturnType<typeof store.getSnapshot>[] = []; const unsubscribe = store.subscribe(() => { snapshots.push(store.getSnapshot()); });
      try {
        sync.receive();
        expect(oldContext.signal.aborted).toBe(true);
        expect(store.getSnapshot()).toMatchObject({ status: 'loading', user: null, tenants: [], activeTenantId: null, error: null });
        identity.resolve(json({ userId: 'user-B', login: 'B' }));
        await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-B' }, tenants: [tenants[1]], activeTenantId: 'B' }));
        const current = store.getSnapshot(); snapshots.length = 0;
        if (decoding) bodyGate.resolve({ old: 'private-invoices' }); else responseGate.resolve(json({ old: 'private-invoices' }, status));
        await rejected;
        expect(store.getSnapshot()).toBe(current); expect(snapshots).toEqual([]); expect(sync.channel.postMessage).not.toHaveBeenCalled();
      } finally { unsubscribe(); sync.disconnect(); }
    });
  it.each(['http', 'network'] as const)('hides old data during visibility verification and offers recovery after a transient %s failure', async mode => {
    const { store, controls, calls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    const gate = deferred<Response>(); controls.meOverride = async () => { await gate.promise; if (mode === 'network') throw new Error('Private detail'); return json({}, 503); };
    try {
      const count = calls.filter(call => call.path.endsWith('/me')).length;
      sync.visibility.show(); sync.visibility.show();
      expect(calls.filter(call => call.path.endsWith('/me'))).toHaveLength(count + 1);
      expect(store.getSnapshot()).toMatchObject({ status: 'loading', user: null, tenants: [], activeTenantId: null });
      gate.resolve(json({})); await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'error', recovery: 'restore', busy: false, user: null, tenants: [] }));
      expect(store.getSnapshot().notice).toBeNull(); expect(sync.channel.postMessage).not.toHaveBeenCalled();
      controls.meOverride = null; await store.restore();
      expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', error: null, user: { userId: 'user-1' } });
    } finally { sync.disconnect(); }
  });
  it.each([401, 200])('announces an identity change found on visibility HTTP %s once, after server confirmation', async status => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    controls.meOverride = async () => json({ userId: 'user-B', login: 'B' }, status); controls.tenantsOverride = async () => json([tenants[1]]);
    try {
      sync.visibility.show();
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: status === 401 ? 'anonymous' : 'authenticated', busy: false }));
      expect(store.getSnapshot().user?.userId ?? null).toBe(status === 401 ? null : 'user-B');
      expect(sync.channel.postMessage).toHaveBeenCalledExactlyOnceWith(sessionChanged);
      sync.visibility.show(); await vi.waitFor(() => expect(store.getSnapshot().busy).toBe(false));
      expect(sync.channel.postMessage).toHaveBeenCalledTimes(1);
    } finally { sync.disconnect(); }
  });
  it('supersedes a pending remote verification but deduplicates visibility events and never echoes remote signals', async () => {
    const { store, controls, calls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    sync.visibility.show(); await vi.waitFor(() => expect(store.getSnapshot().busy).toBe(false));
    const first = deferred<Response>(); const second = deferred<Response>(); let reads = 0;
    controls.meOverride = () => ++reads === 1 ? first.promise : second.promise;
    try {
      sync.receive(); const firstSignal = calls.at(-1)!.init.signal!;
      sync.visibility.show(); expect(reads).toBe(1);
      sync.receive(); expect(firstSignal.aborted).toBe(true); expect(reads).toBe(2);
      first.resolve(json({ userId: 'stale-user', login: 'stale' })); second.resolve(json({ userId: 'current-user', login: 'current' }));
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'current-user' } }));
      expect(sync.channel.postMessage).not.toHaveBeenCalled();
    } finally { sync.disconnect(); }
  });
  it('restarts a pre-existing verification after a real hidden-to-visible transition while old JSON is still decoding', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    const restore = vi.spyOn(store, 'restore');
    const entered = deferred<void>(); const body = deferred<unknown>(); const old = json({});
    old.json = () => { entered.resolve(); return body.promise; }; let reads = 0;
    controls.meOverride = async () => ++reads === 1 ? old : json({ userId: 'user-B', login: 'B' });
    try {
      sync.receive(); const oldVerification = restore.mock.results[0].value; await entered.promise;
      sync.visibility.visibilityState = 'hidden'; sync.visibility.dispatchEvent(new Event('visibilitychange'));
      sync.visibility.show();
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-B' } }));
      expect(reads).toBe(2);
      const current = store.getSnapshot(); body.resolve({ userId: 'stale-A', login: 'A' });
      await oldVerification;
      expect(store.getSnapshot()).toBe(current);
    } finally { sync.disconnect(); restore.mockRestore(); }
  });
  it('announces a new server-confirmed identity even if loading tenant permissions subsequently fails', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password'); const sync = synchronization(store);
    controls.meOverride = async () => json({ userId: 'user-B', login: 'B' }); controls.tenantsStatus = 503;
    try {
      sync.visibility.show();
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'error', user: null, tenants: [], busy: false }));
      expect(sync.channel.postMessage).toHaveBeenCalledExactlyOnceWith(sessionChanged);
      controls.tenantsStatus = 200; await store.restore();
      expect(store.getSnapshot().user?.userId).toBe('user-B'); expect(sync.channel.postMessage).toHaveBeenCalledTimes(1);
    } finally { sync.disconnect(); }
  });
  it('does not interrupt a local login on visibility and removes listeners and closes the channel on cleanup', async () => {
    const { store, controls, calls } = fixture(); await store.restore(); const sync = synchronization(store);
    const identity = deferred<Response>(); controls.meOverride = () => identity.promise;
    const login = store.login('user', 'password');
    await vi.waitFor(() => expect(calls.filter(call => call.path.endsWith('/me'))).toHaveLength(2));
    sync.visibility.show(); expect(calls.filter(call => call.path.endsWith('/me'))).toHaveLength(2);
    sync.disconnect(); expect(sync.channel.close).toHaveBeenCalledTimes(1); expect(sync.channel.onmessage).toBeNull();
    vi.mocked(sync.channel.postMessage).mockClear(); const count = calls.length;
    sync.visibility.show(); expect(calls).toHaveLength(count);
    identity.resolve(json({ userId: 'user-1', login: 'user' })); expect(await login).toBe(true);
    await store.logout(); expect(sync.channel.postMessage).not.toHaveBeenCalled();
  });
  it('retains visibility verification when BroadcastChannel cannot be created', async () => {
    const { store, controls } = fixture(); await store.restore(); await store.login('user', 'password');
    const visibility = new TestVisibility(); const { disconnect: stop } = connectBrowserSession(store, visibility, () => { throw new Error('Unavailable'); });
    try {
      controls.meOverride = async () => json({}, 401); visibility.show();
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', user: null, tenants: [] }));
    } finally { stop(); }
  });
});

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
  it.each([200, 401, 403, 503])('ignores older refreshTenants HTTP %s after a newer successful access refresh, without transient state changes', async status => {
    const { store, controls, context, calls } = fixture();
    await store.restore(); await store.login('user', 'password');
    const pending: ((response: Response) => void)[] = [];
    controls.tenantsOverride = () => new Promise(resolve => { pending.push(resolve); });
    const older = store.refreshTenants();
    const oldSignal = calls.at(-1)!.init.signal!;
    const newer = store.refreshTenants();
    expect(oldSignal.aborted).toBe(true); expect(calls.at(-1)!.init.signal!.aborted).toBe(false);
    expect(pending).toHaveLength(2);
    // The latest response removes tenant A. An older result must not restore it or expire this valid session.
    pending[1](json([tenants[1]])); await newer;
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, tenants: [tenants[1]], activeTenantId: null, error: null });
    const current = store.getSnapshot(); const sessionGeneration = context.session;
    const publications: ReturnType<typeof store.getSnapshot>[] = [];
    const unsubscribe = store.subscribe(() => { publications.push(store.getSnapshot()); });
    try {
      pending[0](json(status === 200 ? tenants : {}, status)); await older;
      expect(store.getSnapshot()).toBe(current);
      expect(publications).toEqual([]);
      expect(context.session).toBe(sessionGeneration);
    } finally { unsubscribe(); }
  });
  it.each([
    { status: 200, rejectBody: false }, { status: 401, rejectBody: false }, { status: 403, rejectBody: false },
    { status: 503, rejectBody: false }, { status: 401, rejectBody: true },
  ])('ignores superseded HTTP $status while its response body is decoding (rejectBody=$rejectBody)', async ({ status, rejectBody }) => {
    const { store, controls, api, calls } = fixture();
    await store.restore(); await store.login('user', 'password');
    const unauthorized = vi.fn(api.onUnauthorized); api.onUnauthorized = unauthorized;
    let entered!: () => void; const decoding = new Promise<void>(resolve => { entered = resolve; });
    let release!: (body: unknown) => void; let reject!: (reason: unknown) => void;
    const body = new Promise<unknown>((resolve, fail) => { release = resolve; reject = fail; });
    const response = json({}, status);
    response.json = () => { entered(); return body; };
    controls.tenantsOverride = async () => response;
    const older = store.refreshTenants(); const oldSignal = calls.at(-1)!.init.signal!;
    await decoding;
    controls.tenantsOverride = async () => json([tenants[1]]);
    await store.refreshTenants(); expect(oldSignal.aborted).toBe(true);
    const current = store.getSnapshot();
    expect(current).toMatchObject({ status: 'authenticated', tenants: [tenants[1]], activeTenantId: null, error: null });
    const publications: ReturnType<typeof store.getSnapshot>[] = [];
    const unsubscribe = store.subscribe(() => { publications.push(store.getSnapshot()); });
    try {
      if (rejectBody) reject(new SyntaxError('Old response body failed')); else release(status === 200 ? tenants : {});
      await older;
      expect(unauthorized).not.toHaveBeenCalled();
      expect(store.getSnapshot()).toBe(current); expect(publications).toEqual([]);
    } finally { unsubscribe(); }
  });
  it('does not let an older completion release the controller of a still-pending refresh', async () => {
    const { store, controls, calls } = fixture();
    await store.restore(); await store.login('user', 'password');
    const pending: ((response: Response) => void)[] = [];
    controls.tenantsOverride = () => new Promise(resolve => { pending.push(resolve); });
    const first = store.refreshTenants(); const second = store.refreshTenants();
    const secondSignal = calls.at(-1)!.init.signal!;
    const before = store.getSnapshot();
    pending[0](json(tenants)); await first;
    expect(store.getSnapshot()).toBe(before); expect(secondSignal.aborted).toBe(false);
    const third = store.refreshTenants(); expect(secondSignal.aborted).toBe(true);
    pending[1](json({}, 401)); await second;
    expect(store.getSnapshot()).toBe(before);
    pending[2](json([tenants[1]])); await third;
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', tenants: [tenants[1]], error: null, notice: null });
  });
  it.each([200, 503])('releases the controller after a completed HTTP %s refresh', async status => {
    const { store, controls, calls } = fixture();
    await store.restore(); await store.login('user', 'password');
    controls.tenantsStatus = status; await store.refreshTenants();
    const completedSignal = calls.at(-1)!.init.signal!;
    expect(completedSignal.aborted).toBe(false);
    controls.tenantsStatus = 200; await store.refreshTenants();
    expect(completedSignal.aborted).toBe(false);
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', tenants, error: null, notice: null });
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
  it.each(['http', 'network'] as const)('recovers explicitly after a /me restore failure (%s), without retaining the previous identity', async mode => {
    const { store, controls, context } = fixture();
    await store.restore(); await store.login('user', 'password'); const previous = context.capture('tenant');
    controls.meOverride = async () => { if (mode === 'network') throw new Error('Private diagnostics'); return json({ detail: 'Private diagnostics' }, 503); };
    const restore = store.restore();
    expect(previous.signal.aborted).toBe(true);
    expect(store.getSnapshot()).toMatchObject({ status: 'loading', user: null, tenants: [], activeTenantId: null, error: null });
    await restore;
    expect(store.getSnapshot()).toMatchObject({ status: 'error', recovery: 'restore', busy: false, user: null, tenants: [], activeTenantId: null });
    expect(store.getSnapshot().error).not.toContain('Private diagnostics');
    controls.meOverride = null; await store.restore();
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, tenants, error: null, notice: null });
  });
  it('recovers without another login after CSRF fails following successful password authentication', async () => {
    const { store, controls, calls } = fixture(); await store.restore();
    let csrfReads = 0;
    controls.csrfOverride = async () => ++csrfReads === 1 ? json({ requestToken: 'anonymous-token', headerName: 'X-CSRF-TOKEN' }) : json({ detail: 'Private diagnostics' }, 503);
    expect(await store.login('user', 'password')).toBe(false);
    expect(csrfReads).toBe(2);
    expect(store.getSnapshot()).toMatchObject({ status: 'error', recovery: 'restore', busy: false, user: null, tenants: [], activeTenantId: null });
    expect(store.getSnapshot().error).toContain('Zalogowano, ale nie udało się wczytać sesji');
    expect(calls.some(call => call.path === '/api/tenants')).toBe(false);
    controls.csrfOverride = null; await store.restore();
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', error: null, notice: null });
    expect(calls.filter(call => call.path.endsWith('/login'))).toHaveLength(1);
  });
  it.each([401, 503])('recovers after tenants HTTP %s following a successful login without publishing partial identity', async status => {
    const { store, controls, calls } = fixture(); await store.restore();
    controls.tenantsStatus = status;
    const publications: ReturnType<typeof store.getSnapshot>[] = [];
    const unsubscribe = store.subscribe(() => { publications.push(store.getSnapshot()); });
    try {
      expect(await store.login('user', 'password')).toBe(false);
      expect(store.getSnapshot()).toMatchObject({ status: status === 401 ? 'anonymous' : 'error', busy: false, user: null, tenants: [], activeTenantId: null });
      expect(publications.every(state => state.user === null && state.tenants.length === 0 && state.activeTenantId === null)).toBe(true);
    } finally { unsubscribe(); }
    controls.tenantsStatus = 200; await store.restore();
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, tenants, error: null, notice: null });
    expect(calls.filter(call => call.path.endsWith('/login'))).toHaveLength(1);
  });
  it.each([401, 503])('handles tenant 403 followed by access refresh HTTP %s without retrying the business request', async status => {
    const { store, controls, api, calls } = fixture(); await store.restore(); await store.login('user', 'password');
    let release!: (response: Response) => void;
    controls.tenantsOverride = () => new Promise(resolve => { release = resolve; });
    controls.businessStatus = 403;
    await expect(api.request('/api/business', { scope: 'tenant' })).rejects.toMatchObject({ status: 403 });
    expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', activeTenantId: null, error: null });
    release(json({}, status));
    if (status === 401) {
      await vi.waitFor(() => expect(store.getSnapshot()).toMatchObject({ status: 'anonymous', user: null, tenants: [], activeTenantId: null, error: null }));
      expect(store.getSnapshot().notice).toContain('Sesja wygasła');
    } else {
      await vi.waitFor(() => expect(store.getSnapshot().error).toContain('Usługa jest chwilowo niedostępna'));
      expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', user: { userId: 'user-1' }, activeTenantId: null });
      controls.tenantsOverride = async () => json([tenants[1]]); await store.refreshTenants();
      expect(store.getSnapshot()).toMatchObject({ status: 'authenticated', tenants: [tenants[1]], activeTenantId: null, error: null });
    }
    expect(calls.filter(call => call.path === '/api/business')).toHaveLength(1);
  });
});
