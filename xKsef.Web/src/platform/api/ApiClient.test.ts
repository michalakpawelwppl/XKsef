import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from './ApiClient';
import type { Fetch } from './ApiClient';
import { ApiError, retryAfterSeconds } from './ApiError';
import { RequestContext, StaleContextError } from '../session/RequestContext';
const json = (body: unknown, status = 200, headers = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
const csrf = () => json({ requestToken: 'csrf-secret', headerName: 'X-CSRF-TOKEN' });
function setup(fetcher: Fetch) { const context = new RequestContext(); context.selectTenant('A'); return { context, api: new ApiClient(context, fetcher) }; }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }

describe('API transport and CSRF', () => {
  it('uses same-origin cookies and captures the tenant, without Authorization', async () => {
    const fetch = vi.fn<Fetch>(async () => json({ ok: true })); const { api } = setup(fetch);
    await api.request('/api/data', { scope: 'tenant' });
    const init = fetch.mock.calls[0][1]!;
    expect(init.credentials).toBe('same-origin'); expect(init.cache).toBe('no-store'); expect(init.redirect).toBe('error');
    expect(new Headers(init.headers).get('X-Tenant-Id')).toBe('A'); expect(new Headers(init.headers).has('Authorization')).toBe(false);
  });
  it('anonymous and session requests carry no tenant header', async () => {
    const fetch = vi.fn<Fetch>(async () => json({})); const { api } = setup(fetch);
    await api.request('/api/browser-auth/me', { scope: 'anonymous' }); await api.request('/api/tenants', { scope: 'session' });
    for (const [, init] of fetch.mock.calls) expect(new Headers(init!.headers).has('X-Tenant-Id')).toBe(false);
  });
  it('deduplicates CSRF acquisition and refreshes only in memory', async () => {
    let tokens = 0; const calls: RequestInit[] = [];
    const { api } = setup(async (path, init) => { if (path === '/api/browser-auth/csrf') { tokens++; return csrf(); } calls.push(init!); return json({}); });
    await Promise.all([api.request('/api/create', { scope: 'tenant', method: 'POST' }), api.request('/api/create', { scope: 'tenant', method: 'POST' })]);
    expect(tokens).toBe(1); calls.forEach(init => expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('csrf-secret'));
    await api.refreshCsrf(); expect(tokens).toBe(2);
  });
  it.each([400, 401, 403, 429, 503])('never retries a mutation after HTTP %s', async status => {
    let posts = 0;
    const { api } = setup(async path => path === '/api/browser-auth/csrf' ? csrf() : (posts++, json({ detail: 'SQL secret', errors: { Identity: ['Validation error'] } }, status, { 'Retry-After': '30' })));
    try { await api.request('/api/create', { scope: 'tenant', method: 'POST' }); throw new Error('Expected rejection'); }
    catch (error) { expect(error).toBeInstanceOf(ApiError); expect((error as ApiError).status).toBe(status); expect((error as ApiError).message).not.toContain('SQL secret'); if (status === 429) expect((error as ApiError).retryAfterSeconds).toBe(30); }
    expect(posts).toBe(1);
  });
  it('exposes Identity validation messages without exposing response details', async () => {
    const { api } = setup(async path => path === '/api/browser-auth/csrf' ? csrf() : json({ detail: 'SQL detail', errors: { Identity: ['Duplicate email'] } }, 400));
    await expect(api.request('/api/register', { scope: 'anonymous', method: 'POST' })).rejects.toMatchObject({ validation: ['Duplicate email'] });
  });
  it('invalidates a rejected CSRF token but never replays the failed write', async () => {
    let tokens = 0; let posts = 0;
    const { api } = setup(async path => {
      if (path === '/api/browser-auth/csrf') { tokens++; return csrf(); }
      posts++; return json({ title: 'Invalid CSRF' }, 400);
    });
    await expect(api.request('/api/create', { scope: 'tenant', method: 'POST' })).rejects.toMatchObject({ status: 400 });
    expect(posts).toBe(1); expect(tokens).toBe(1);
    await expect(api.request('/api/create', { scope: 'tenant', method: 'POST' })).rejects.toMatchObject({ status: 400 });
    expect(posts).toBe(2); expect(tokens).toBe(2);
  });
  it('does not send a mutation if CSRF is unavailable or malformed', async () => {
    const fetch = vi.fn<Fetch>(async () => json({ requestToken: 'bad', headerName: 'Other' })); const { api } = setup(fetch);
    await expect(api.request('/api/create', { scope: 'tenant', method: 'POST' })).rejects.toMatchObject({ status: 502 }); expect(fetch).toHaveBeenCalledTimes(1);
  });
  it.each(['https://external.invalid/api/x', '//external.invalid/api/x', '/api/../private', '/api/x#fragment'])('rejects non API/same-origin path %s', async path => {
    const fetch = vi.fn<Fetch>(); const { api } = setup(fetch);
    await expect(api.request(path, { scope: 'session' })).rejects.toThrow(); expect(fetch).not.toHaveBeenCalled();
  });
  it('requires an explicit selected tenant for tenant requests', async () => {
    const fetch = vi.fn<Fetch>(); const { api, context } = setup(fetch); context.selectTenant(null);
    await expect(api.request('/api/data', { scope: 'tenant' })).rejects.toThrow('Najpierw wybierz tenanta'); expect(fetch).not.toHaveBeenCalled();
  });
  it('maps network errors safely and parses Retry-After', async () => {
    const { api } = setup(async () => { throw new Error('private server diagnostics'); });
    await expect(api.request('/api/data', { scope: 'session' })).rejects.toMatchObject({ status: 0 });
    expect(retryAfterSeconds('invalid')).toBeNull(); expect(retryAfterSeconds('12')).toBe(12);
  });
});

describe('context generations', () => {
  it('aborts tenant requests on A → B → A but leaves session requests current', () => {
    const { context } = setup(vi.fn()); const tenant = context.capture('tenant'); const session = context.capture('session');
    context.selectTenant('B'); context.selectTenant('A'); expect(tenant.signal.aborted).toBe(true); expect(context.isCurrent(tenant)).toBe(false); expect(context.isCurrent(session)).toBe(true);
    context.newSession(); expect(session.signal.aborted).toBe(true); expect(context.isCurrent(session)).toBe(false);
  });
  it.each([200, 401, 403])('ignores late HTTP %s even if transport ignores cancellation', async status => {
    const pending = deferred<Response>(); const { api, context } = setup(async () => pending.promise);
    const unauthorized = vi.fn(); const forbidden = vi.fn(); api.onUnauthorized = unauthorized; api.onTenantForbidden = forbidden;
    const result = api.request('/api/data', { scope: 'tenant' }); const rejection = expect(result).rejects.toBeInstanceOf(StaleContextError);
    context.selectTenant('B'); context.selectTenant('A'); pending.resolve(json({}, status)); await rejection;
    expect(unauthorized).not.toHaveBeenCalled(); expect(forbidden).not.toHaveBeenCalled();
  });
  it('checks context again after asynchronous body decoding', async () => {
    const body = deferred<unknown>(); const response = json({}); response.json = () => body.promise;
    const { api, context } = setup(async () => response); const result = api.request('/api/data', { scope: 'tenant' });
    await vi.waitFor(() => expect(response.bodyUsed).toBe(false)); await Promise.resolve();
    const rejection = expect(result).rejects.toBeInstanceOf(StaleContextError); context.newSession(); body.resolve({ old: true }); await rejection;
  });
  it('clears a cached CSRF token after a session change', async () => {
    let tokens = 0; const { api, context } = setup(async path => path === '/api/browser-auth/csrf' ? (tokens++, csrf()) : json({}));
    await api.request('/api/action', { scope: 'anonymous', method: 'POST' }); context.newSession();
    await api.request('/api/action', { scope: 'anonymous', method: 'POST' }); expect(tokens).toBe(2);
  });
  it('does not send a mutation whose context changes while fetching CSRF', async () => {
    const pending = deferred<Response>(); const fetch = vi.fn<Fetch>(async () => pending.promise); const { api, context } = setup(fetch);
    const result = api.request('/api/action', { scope: 'tenant', method: 'POST' }); const rejection = expect(result).rejects.toBeInstanceOf(StaleContextError);
    context.selectTenant('B'); pending.resolve(csrf()); await rejection; expect(fetch).toHaveBeenCalledTimes(1);
  });
  it('propagates explicit cancellation without translating it to a network failure', async () => {
    const { api } = setup(async () => { throw new DOMException('Cancelled', 'AbortError'); });
    await expect(api.request('/api/data', { scope: 'session' })).rejects.toMatchObject({ name: 'AbortError' });
  });
});
