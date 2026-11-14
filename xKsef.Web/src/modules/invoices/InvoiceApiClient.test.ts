import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../../platform/api/ApiClient';
import type { Fetch } from '../../platform/api/ApiClient';
import { ApiError } from '../../platform/api/ApiError';
import { RequestContext, StaleContextError } from '../../platform/session/RequestContext';
import { InvoiceApiClient, mapInvoiceToTable } from './InvoiceApiClient';
import { readInvoiceQueryResult } from './InvoiceApiContracts';
import type { InvoiceDto, InvoiceQuery } from './InvoiceApiContracts';

const query: InvoiceQuery = { from: '2026-11-01', to: '2026-11-14', direction: 'Sales', dateKind: 'IssueDate' };
const invoice: InvoiceDto = {
  ksefNumber: 'KSEF-1', invoiceNumber: 'FV/1', sellerName: 'Sprzedawca', sellerNip: '1234567890',
  buyerName: null, buyerNip: null, issueDate: '2026-11-01', invoicingDate: '2026-11-02T23:45:00.1234567+01:00',
  currency: 'PLN', grossAmount: -123, netAmount: null, vatAmount: 0, vatAmountPln: 0,
  grossAmountExact: '-123.00', netAmountExact: null, vatAmountExact: '0.00', vatAmountPlnExact: '0.00',
};
const summary = { currency: 'PLN', invoiceCount: 1, netTotal: null, vatTotal: 0, grossTotal: -123, missingNetCount: 1, missingVatCount: 0 };
const payload = () => ({ items: [{ ...invoice }], summaries: [{ ...summary }] });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
function setup(fetcher: Fetch = async () => json(payload())) {
  const fetch = vi.fn<Fetch>(fetcher); const context = new RequestContext(); context.selectTenant('tenant-A');
  const api = new ApiClient(context, fetch);
  return { client: new InvoiceApiClient(api), api, context, fetch };
}
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }

describe('invoice query transport and table mapping', () => {
  it.each(['Sales', 'Purchase'] as const)('queries %s using the existing tenant transport and preserves every DTO field', async direction => {
    const { client, fetch } = setup(); const result = await client.queryInvoices({ ...query, direction });
    const [path, init] = fetch.mock.calls[0]; const url = new URL(String(path), 'https://localhost');
    expect(url.pathname).toBe('/api/invoices/query');
    expect(Object.fromEntries(url.searchParams)).toEqual({ from: query.from, to: query.to, direction, dateKind: 'IssueDate' });
    expect(init).toMatchObject({ method: 'GET', credentials: 'same-origin', cache: 'no-store' });
    const headers = new Headers(init!.headers);
    expect(headers.get('X-Tenant-Id')).toBe('tenant-A'); expect(headers.has('Authorization')).toBe(false); expect(headers.has('X-CSRF-TOKEN')).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(1);
    const row = result.items[0];
    expect(row).toMatchObject(invoice); expect(row.direction).toBe(direction === 'Sales' ? 'sales' : 'purchase');
    expect(row.netAmount).toBeNull(); expect(row.vatAmount).toBe(0); expect(row.vatAmountPln).toBe(0);
    expect(result.summaries).toEqual([summary]);
  });
  it('encodes the required timezone without changing the timestamp or offset in the response', async () => {
    const { client, fetch } = setup(); const result = await client.queryInvoices({ ...query, dateKind: 'InvoicingDate', timeZoneId: 'Europe/Warsaw' });
    expect(String(fetch.mock.calls[0][0])).toContain('timeZoneId=Europe%2FWarsaw');
    expect(result.items[0].invoicingDate).toBe(invoice.invoicingDate);
  });
  it('does not send an irrelevant timezone for issue dates', async () => {
    const { client, fetch } = setup(); await client.queryInvoices({ ...query, timeZoneId: 'Europe/Warsaw' });
    expect(String(fetch.mock.calls[0][0])).not.toContain('timeZoneId');
  });
  it('rejects the unsupported KSeF upper boundary without sending a request', async () => {
    const { client, fetch } = setup();
    await expect(client.queryInvoices({ ...query, from: '9999-12-30', to: '9999-12-31', dateKind: 'InvoicingDate', timeZoneId: 'Europe/Warsaw' })).rejects.toThrow('31.12.9999');
    expect(fetch).not.toHaveBeenCalled();
  });
  it('sends an inclusive single-day issue-date range without converting it to UTC', async () => {
    const { client, fetch } = setup(); await client.queryInvoices({ ...query, from: '2024-02-29', to: '2024-02-29' });
    const params = new URL(String(fetch.mock.calls[0][0]), 'https://localhost').searchParams;
    expect(params.get('from')).toBe('2024-02-29'); expect(params.get('to')).toBe('2024-02-29'); expect(params.has('timeZoneId')).toBe(false);
  });
  it('keeps row identity distinct across tenants and directions', () => {
    const first = mapInvoiceToTable(invoice, 'tenant-A', 'Sales');
    expect(first.id).not.toBe(mapInvoiceToTable(invoice, 'tenant-B', 'Sales').id);
    expect(first.id).not.toBe(mapInvoiceToTable(invoice, 'tenant-A', 'Purchase').id);
    expect(first.id).toBe(mapInvoiceToTable({ ...invoice }, 'tenant-A', 'Sales').id);
  });
  it.each([
    { from: '2026-02-30' }, { from: '0000-01-01' }, { to: '2026-10-01' }, { direction: 'Other' },
    { dateKind: 'Other' }, { dateKind: 'InvoicingDate' }, { dateKind: 'InvoicingDate', timeZoneId: ' ' },
  ])('rejects invalid filters before transport: %j', async patch => {
    const { client, fetch } = setup();
    await expect(client.queryInvoices({ ...query, ...patch } as InvoiceQuery)).rejects.toThrow(); expect(fetch).not.toHaveBeenCalled();
  });
  it('requires an active tenant without bypassing RequestContext', async () => {
    const { client, context, fetch } = setup(); context.selectTenant(null);
    await expect(client.queryInvoices(query)).rejects.toThrow('Najpierw wybierz tenanta'); expect(fetch).not.toHaveBeenCalled();
  });
  it.each([400, 401, 403, 429, 503])('preserves HTTP %s and existing session callbacks without retries', async status => {
    const { client, api, fetch } = setup(async () => json({ detail: 'private SQL information' }, status));
    api.onUnauthorized = vi.fn(); api.onTenantForbidden = vi.fn();
    await expect(client.queryInvoices(query)).rejects.toMatchObject({ status });
    expect(api.onUnauthorized).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
    expect(api.onTenantForbidden).toHaveBeenCalledTimes(status === 403 ? 1 : 0); expect(fetch).toHaveBeenCalledTimes(1);
  });
});

describe('invoice response validation', () => {
  it('preserves exact amount strings even when JSON numbers have already lost cents', () => {
    const items = [{ ...invoice, grossAmount: Number('9999999999999999.99'), grossAmountExact: '9999999999999999.99' }];
    expect(readInvoiceQueryResult({ ...payload(), items }).items[0].grossAmountExact).toBe('9999999999999999.99');
  });
  it.each([{ grossAmountExact: undefined }, { grossAmountExact: null }, { grossAmountExact: '-123.001' },
    { grossAmountExact: '-122.00' }, { netAmountExact: '0.00' }, { vatAmountExact: null }, { vatAmountExact: 0 }])('rejects missing, inconsistent or malformed exact fields: %j', patch => {
    expect(() => readInvoiceQueryResult({ ...payload(), items: [{ ...invoice, ...patch }] })).toThrow(ApiError);
  });
  it('accepts empty arrays and nullable fields without inventing values', () => {
    expect(readInvoiceQueryResult({ items: [], summaries: [] })).toEqual({ items: [], summaries: [] });
    const value = payload(); value.items[0] = { ...invoice, invoicingDate: null, netAmount: 0, vatAmount: null, vatAmountPln: null, netAmountExact: '0.00', vatAmountExact: null, vatAmountPlnExact: null };
    expect(readInvoiceQueryResult(value).items[0]).toEqual(value.items[0]);
  });
  it.each([null, [], {}, { items: [], summaries: null }, { items: null, summaries: [] }, { items: [], summaries: {} }])('rejects malformed envelopes: %j', value => {
    expect(() => readInvoiceQueryResult(value)).toThrow(ApiError);
  });
  it.each([
    { ksefNumber: '' }, { invoiceNumber: 123 }, { sellerName: null }, { sellerNip: null }, { buyerName: 123 }, { buyerNip: false },
    { issueDate: '2026-02-30' }, { issueDate: '2026-11-01T00:00:00Z' }, { invoicingDate: '2026-11-02T23:45:00' },
    { invoicingDate: '2026-02-30T12:00:00Z' }, { invoicingDate: '2026-11-02T25:00:00Z' }, { invoicingDate: '2026-11-02T12:00:00+15:00' },
    { grossAmount: null }, { grossAmount: Infinity }, { currency: null }, { netAmount: '0' }, { vatAmount: undefined }, { vatAmountPln: NaN },
  ])('rejects invalid or missing invoice fields: %j', patch => {
    expect(() => readInvoiceQueryResult({ ...payload(), items: [{ ...invoice, ...patch }] })).toThrow(ApiError);
  });
  it.each([
    { currency: null }, { invoiceCount: -1 }, { invoiceCount: 1.5 }, { netTotal: '0' }, { vatTotal: undefined },
    { grossTotal: null }, { grossTotal: NaN }, { missingNetCount: 2 }, { missingVatCount: -1 },
  ])('rejects malformed summaries: %j', patch => {
    expect(() => readInvoiceQueryResult({ ...payload(), summaries: [{ ...summary, ...patch }] })).toThrow(ApiError);
  });
  it('rejects duplicate invoice identities and duplicate currency summaries', () => {
    expect(() => readInvoiceQueryResult({ ...payload(), items: [invoice, invoice] })).toThrow(ApiError);
    expect(() => readInvoiceQueryResult({ ...payload(), summaries: [summary, summary] })).toThrow(ApiError);
  });
  it('validates successful HTTP responses instead of trusting a TypeScript cast', async () => {
    const { client } = setup(async () => json({ items: [], summaries: 'private data' }));
    await expect(client.queryInvoices(query)).rejects.toMatchObject({ status: 502, message: 'Nieprawidłowa odpowiedź API dotycząca faktur.' });
  });
});

describe('invoice request cancellation and tenant isolation', () => {
  it.each(['range', 'direction'])('ignores old 401 after changing %s in the same tenant', async change => {
    const pending = deferred<Response>(); let requests = 0;
    const { client, api, fetch } = setup(async () => ++requests === 1 ? pending.promise : json(payload()));
    api.onUnauthorized = vi.fn(); api.onTenantForbidden = vi.fn();
    const controller = new AbortController(); const old = client.queryInvoices(query, controller.signal);
    const rejected = expect(old).rejects.toMatchObject({ name: 'AbortError' }); controller.abort();
    const next: InvoiceQuery = change === 'range' ? { ...query, from: '2026-11-02' } : { ...query, direction: 'Purchase' };
    const current = await client.queryInvoices(next); pending.resolve(json({}, 401)); await rejected;
    expect(current.items[0].direction).toBe(change === 'direction' ? 'purchase' : 'sales');
    const url = new URL(String(fetch.mock.calls[1][0]), 'https://localhost');
    expect(url.searchParams.get('from')).toBe(next.from); expect(url.searchParams.get('direction')).toBe(next.direction);
    expect(api.onUnauthorized).not.toHaveBeenCalled(); expect(api.onTenantForbidden).not.toHaveBeenCalled();
  });
  it('does not send an already cancelled query', async () => {
    const { client, fetch } = setup(); const controller = new AbortController(); controller.abort();
    await expect(client.queryInvoices(query, controller.signal)).rejects.toMatchObject({ name: 'AbortError' }); expect(fetch).not.toHaveBeenCalled();
  });
  it('passes cancellation through the existing transport', async () => {
    const { client, fetch } = setup((_path, init) => new Promise((_resolve, reject) => {
      init!.signal!.addEventListener('abort', () => reject(new DOMException('Cancelled', 'AbortError')), { once: true });
    }));
    const controller = new AbortController(); const result = client.queryInvoices(query, controller.signal);
    const rejected = expect(result).rejects.toMatchObject({ name: 'AbortError' }); controller.abort(); await rejected;
    expect(fetch.mock.calls[0][1]!.signal!.aborted).toBe(true);
  });
  it('ignores results when explicit cancellation is ignored by the transport', async () => {
    const pending = deferred<Response>(); const { client } = setup(async () => pending.promise);
    const controller = new AbortController(); const result = client.queryInvoices(query, controller.signal);
    const rejected = expect(result).rejects.toMatchObject({ name: 'AbortError' }); controller.abort(); pending.resolve(json(payload())); await rejected;
  });
  it.each([200, 401, 403])('rejects late HTTP %s on A → B → A without affecting the new context', async status => {
    const pending = deferred<Response>(); const { client, api, context, fetch } = setup(async () => pending.promise);
    api.onUnauthorized = vi.fn(); api.onTenantForbidden = vi.fn();
    const result = client.queryInvoices(query); const rejected = expect(result).rejects.toBeInstanceOf(StaleContextError);
    context.selectTenant('tenant-B'); context.selectTenant('tenant-A'); pending.resolve(json(payload(), status)); await rejected;
    expect(fetch.mock.calls[0][1]!.signal!.aborted).toBe(true);
    expect(api.onUnauthorized).not.toHaveBeenCalled(); expect(api.onTenantForbidden).not.toHaveBeenCalled();
  });
  it('rejects a session change while JSON decoding is pending', async () => {
    const body = deferred<unknown>(); const entered = deferred<void>(); const response = json({});
    response.json = () => { entered.resolve(); return body.promise; };
    const { client, context } = setup(async () => response); const result = client.queryInvoices(query);
    const rejected = expect(result).rejects.toBeInstanceOf(StaleContextError);
    await entered.promise; context.newSession(); body.resolve(payload()); await rejected;
  });
});
