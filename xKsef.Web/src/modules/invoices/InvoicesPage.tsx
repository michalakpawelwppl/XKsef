import { useCallback, useState } from 'react';
import { useSession } from '../../platform/session/SessionProvider';
import type { ApiInvoiceDirection, InvoiceQuery } from './InvoiceApiContracts';
import { InvoiceFilters } from './InvoiceFilters';
import type { InvoiceDateFilters } from './InvoiceFilters';
import { InvoiceTable } from './InvoiceTable';
import { InvoiceSummaries } from './InvoiceSummaries';
import type { ApiInvoiceRow } from './InvoiceApiClient';
import { defaultInvoiceQuery, useInvoices } from './useInvoices';

export function InvoicesPage({ direction }: { readonly direction: ApiInvoiceDirection }) {
  const { state: session } = useSession();
  const [query, setQuery] = useState<InvoiceQuery>(() => defaultInvoiceQuery(direction));
  const result = useInvoices(query);
  const [filtered, setFiltered] = useState<{ key: string; items: readonly ApiInvoiceRow[] } | null>(null);
  const onFilteredRowsChange = useCallback((items: readonly ApiInvoiceRow[]) => setFiltered({ key: result.key, items }), [result.key]);
  function applyFilters(filters: InvoiceDateFilters) {
    setQuery({ from: filters.from, to: filters.to, dateKind: filters.dateKind, direction,
      ...(filters.dateKind === 'InvoicingDate' ? { timeZoneId: 'Europe/Warsaw' } : {}) });
    result.reload();
  }
  const [tableRevision, setTableRevision] = useState(0);
  const title = direction === 'Sales' ? 'Faktury sprzedaży' : 'Faktury zakupu';
  if (!session.activeTenantId) return <main><h1>{title}</h1><p>{session.tenants.length ? 'Wybierz tenanta, aby otworzyć moduł.' : 'Brak dostępnych tenantów. Uzyskaj dostęp do firmy lub utwórz ją w aplikacji WPF.'}</p></main>;
  return <main>
    <div className="breadcrumb">Dokumenty <span aria-hidden="true">/</span> {title}</div>
    <div className="page-heading"><div><h1>{title}</h1><p>Faktury zapisane w lokalnej bazie xKsef.</p></div>
      <button className="secondary-button" disabled={result.status === 'loading'} onClick={result.reload}>{result.status === 'error' ? 'Spróbuj ponownie' : 'Odśwież listę'}</button>
    </div>
    <InvoiceFilters query={query} onApply={applyFilters} />
    {result.status === 'loading' && <p role="status">Wczytywanie faktur…</p>}
    {result.status === 'error' && <p className="error-message" role="alert">{result.error}</p>}
    {result.status === 'ready' && <>
      {result.items.length === 0 && <p role="status">Brak faktur w wybranym okresie.</p>}
      {result.items.length > 0 && <InvoiceTable key={`${result.key}:${tableRevision}`} invoices={result.items} direction={direction === 'Sales' ? 'sales' : 'purchase'} onReset={() => setTableRevision(value => value + 1)} onFilteredRowsChange={onFilteredRowsChange} />}
      <InvoiceSummaries invoices={filtered?.key === result.key ? filtered.items : result.items} />
    </>}
  </main>;
}
