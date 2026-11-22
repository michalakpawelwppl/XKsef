import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../../platform/api/ApiClient';
import type { Fetch } from '../../platform/api/ApiClient';
import { RequestContext, StaleContextError } from '../../platform/session/RequestContext';
import { ContractorApiClient } from './ContractorApiClient';
import { toContractorForm } from './ContractorFormModel';
import { toContractorRequest } from './ContractorFormValidation';
import type { ContractorDto } from './ContractorApiContracts';

const id = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const dto: ContractorDto = { id, createdAt: '2026-11-22T12:00:00Z', isActive: false, fullName: 'Firma', shortName: 'F', nip: null, regon: null, krsNumber: null, euVatNumber: null, otherTaxNumber: null, isEuVatRegistered: false, addresses: [] };
const request = toContractorRequest(toContractorForm(dto));
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const csrf = () => json({ requestToken: 'test-token', headerName: 'X-CSRF-TOKEN' });
function setup(fetcher: Fetch = async path => String(path).endsWith('/csrf') ? csrf() : json(dto)) {
  const fetch = vi.fn<Fetch>(fetcher); const context = new RequestContext(); context.selectTenant('tenant-A');
  const api = new ApiClient(context, fetch);
  return { client: new ContractorApiClient(api), api, context, fetch };
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }

describe('contractor API transport', () => {
  it('gets a single parsed contractor with tenant scope and cookie authentication', async () => {
    const { client, fetch } = setup(); expect(await client.getContractor(id)).toEqual(dto);
    expect(fetch).toHaveBeenCalledTimes(1);
    const [path, init] = fetch.mock.calls[0]; expect(path).toBe(`/api/contractors/${id}`);
    expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin', cache: 'no-store' });
    const headers = new Headers(init!.headers);
    expect(headers.get('X-Tenant-Id')).toBe('tenant-A'); expect(headers.has('Authorization')).toBe(false); expect(headers.has('X-CSRF-TOKEN')).toBe(false);
  });
  it('puts the complete request with unchanged inactive status, tenant and CSRF and accepts 204', async () => {
    const { client, fetch } = setup(async path => String(path).endsWith('/csrf') ? csrf() : new Response(null, { status: 204 }));
    await expect(client.updateContractor(id, request)).resolves.toBeUndefined();
    expect(fetch).toHaveBeenCalledTimes(2);
    const [path, init] = fetch.mock.calls[1]; expect(path).toBe(`/api/contractors/${id}`);
    expect(init).toMatchObject({ method: 'PUT', credentials: 'same-origin', body: JSON.stringify(request) });
    const headers = new Headers(init!.headers);
    expect(headers.get('X-Tenant-Id')).toBe('tenant-A'); expect(headers.get('X-CSRF-TOKEN')).toBe('test-token'); expect(headers.get('Content-Type')).toBe('application/json'); expect(headers.has('Authorization')).toBe(false);
  });
  it('preserves the existing create transport', async () => {
    const { client, fetch } = setup(async path => String(path).endsWith('/csrf') ? csrf() : json({ id }, 201));
    await client.createContractor(request);
    expect(fetch.mock.calls[1][0]).toBe('/api/contractors'); expect(fetch.mock.calls[1][1]?.method).toBe('POST');
  });
  it.each(['get', 'put'] as const)('requires a tenant and respects pre-aborted signals for %s', async method => {
    const { client, context, fetch } = setup();
    const invoke = (signal?: AbortSignal) => method === 'get' ? client.getContractor(id, signal) : client.updateContractor(id, request, signal);
    const controller = new AbortController(); controller.abort();
    await expect(invoke(controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
    context.selectTenant(null); await expect(invoke()).rejects.toThrow('Najpierw wybierz tenanta'); expect(fetch).not.toHaveBeenCalled();
  });
  it.each([400, 401, 403, 404, 500])('preserves HTTP %s and session callbacks without retrying PUT', async status => {
    const { client, api, fetch } = setup(async path => String(path).endsWith('/csrf') ? csrf() : json({}, status));
    api.onUnauthorized = vi.fn(); api.onTenantForbidden = vi.fn();
    await expect(client.updateContractor(id, request)).rejects.toMatchObject({ status });
    expect(api.onUnauthorized).toHaveBeenCalledTimes(status === 401 ? 1 : 0); expect(api.onTenantForbidden).toHaveBeenCalledTimes(status === 403 ? 1 : 0);
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it('propagates missing-record and malformed-response errors from GET', async () => {
    const missing = setup(async () => json({}, 404)); await expect(missing.client.getContractor(id)).rejects.toMatchObject({ status: 404 });
    const malformed = setup(async () => json({})); await expect(malformed.client.getContractor(id)).rejects.toMatchObject({ status: 502 });
  });
  it.each(['get', 'put'] as const)('does not start %s while the session guard blocks business requests', async method => {
    const { client, api, fetch } = setup(); api.beforeBusinessRequest = () => { throw new Error('verification'); };
    await expect(method === 'get' ? client.getContractor(id) : client.updateContractor(id, request)).rejects.toThrow('verification'); expect(fetch).not.toHaveBeenCalled();
  });
  it.each(['tenant', 'session'] as const)('does not PUT after %s changes during CSRF acquisition', async change => {
    const gate = deferred<Response>(); const { client, context, fetch } = setup(() => gate.promise);
    const pending = client.updateContractor(id, request);
    const rejected = expect(pending).rejects.toBeInstanceOf(StaleContextError);
    if (change === 'tenant') context.selectTenant('tenant-B'); else context.newSession();
    gate.resolve(csrf()); await rejected; expect(fetch).toHaveBeenCalledTimes(1);
  });
  it.each(['get', 'put'] as const)('discards a late %s response after tenant A → B → A', async method => {
    const gate = deferred<Response>(); const arrived = deferred<void>();
    let signal: AbortSignal | null | undefined;
    const { client, context } = setup(async (path, init) => {
      if (String(path).endsWith('/csrf')) return csrf();
      signal = init?.signal; arrived.resolve(); return gate.promise;
    });
    const pending = method === 'get' ? client.getContractor(id) : client.updateContractor(id, request);
    const rejected = expect(pending).rejects.toBeInstanceOf(StaleContextError);
    await arrived.promise; context.selectTenant('tenant-B'); context.selectTenant('tenant-A');
    expect(signal?.aborted).toBe(true);
    gate.resolve(method === 'get' ? json(dto) : new Response(null, { status: 204 })); await rejected;
  });
  it('discards a late GET response after logout/session invalidation', async () => {
    const gate = deferred<Response>(); const { client, context } = setup(() => gate.promise);
    const rejected = expect(client.getContractor(id)).rejects.toBeInstanceOf(StaleContextError);
    context.newSession(); gate.resolve(json(dto)); await rejected;
  });
  it('respects caller cancellation while a PUT is pending', async () => {
    const gate = deferred<Response>(); const arrived = deferred<void>(); const controller = new AbortController();
    const { client } = setup(async path => { if (String(path).endsWith('/csrf')) return csrf(); arrived.resolve(); return gate.promise; });
    const rejected = expect(client.updateContractor(id, request, controller.signal)).rejects.toMatchObject({ name: 'AbortError' });
    await arrived.promise; controller.abort(); gate.resolve(new Response(null, { status: 204 })); await rejected;
  });
});
