import type { ApiClient } from '../../platform/api/ApiClient';
import type { Invoice } from './Invoice';
import { invoiceDateFilterError, readInvoiceQueryResult } from './InvoiceApiContracts';
import type { ApiInvoiceDirection, InvoiceDto, InvoiceQuery, InvoiceQueryResultDto } from './InvoiceApiContracts';

// Adds table identity/direction while preserving every API field, including VAT in PLN.
export interface ApiInvoiceRow extends Invoice, InvoiceDto {}
export interface InvoiceTableQueryResult extends InvoiceQueryResultDto { readonly items: readonly ApiInvoiceRow[] }

export function mapInvoiceToTable(invoice: InvoiceDto, tenantId: string, direction: ApiInvoiceDirection): ApiInvoiceRow {
  if (!tenantId || direction !== 'Sales' && direction !== 'Purchase') throw new Error('Nieprawidłowy kontekst faktur.');
  return { ...invoice, id: JSON.stringify([tenantId, direction, invoice.ksefNumber]), direction: direction === 'Sales' ? 'sales' : 'purchase' };
}

function queryPath(query: InvoiceQuery): string {
  const error = invoiceDateFilterError(query);
  if (error) throw new Error(error);
  if (query.direction !== 'Sales' && query.direction !== 'Purchase') throw new Error('Nieprawidłowy kierunek faktur.');
  const params = new URLSearchParams({ from: query.from, to: query.to, direction: query.direction, dateKind: query.dateKind });
  if (query.dateKind === 'InvoicingDate') {
    if (!query.timeZoneId?.trim()) throw new Error('Data przyjęcia do KSeF wymaga strefy czasowej.');
    params.set('timeZoneId', query.timeZoneId);
  }
  return `/api/invoices/query?${params}`;
}

// Pass the existing api returned by useSession(); this client creates no transport or session.
export class InvoiceApiClient {
  constructor(private readonly api: ApiClient) {}

  async queryInvoices(query: InvoiceQuery, signal?: AbortSignal): Promise<InvoiceTableQueryResult> {
    const path = queryPath(query);
    const direction = query.direction;
    signal?.throwIfAborted();
    const context = this.api.context.capture('tenant');
    const response = await this.api.request<unknown>(path, { scope: 'tenant', signal });
    // Also cover explicit cancellation if a transport/body reader ignores its signal.
    signal?.throwIfAborted();
    this.api.context.assertCurrent(context);
    const result = readInvoiceQueryResult(response);
    return { items: result.items.map(invoice => mapInvoiceToTable(invoice, context.tenantId!, direction)), summaries: result.summaries };
  }
}
