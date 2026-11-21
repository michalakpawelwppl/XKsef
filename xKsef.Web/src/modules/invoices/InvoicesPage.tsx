import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSession } from '../../platform/session/SessionProvider';
import type { ApiInvoiceDirection, InvoiceQuery } from './InvoiceApiContracts';
import { InvoiceFilters } from './InvoiceFilters';
import type { InvoiceDateFilters } from './InvoiceFilters';
import { InvoiceTable } from './InvoiceTable';
import { InvoiceSummaries } from './InvoiceSummaries';
import type { ApiInvoiceRow } from './InvoiceApiClient';
import { defaultInvoiceQuery, useInvoices } from './useInvoices';
import { InvoiceApiClient } from './InvoiceApiClient';
import { errorMessage } from '../../platform/api/ApiError';
import { StaleContextError } from '../../platform/session/RequestContext';
import { InvoicePreviewManager } from './InvoicePreviewManager';

export function InvoicesPage({ direction }: { readonly direction: ApiInvoiceDirection }) {
  const { api, state: session } = useSession();
  const invoiceClient = useMemo(() => new InvoiceApiClient(api), [api]);
  const previewManager = useMemo(() => new InvoicePreviewManager(api.context), [api]);

useEffect(() => {
  return () => previewManager.dispose();
}, [previewManager]);

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

  function previewInvoice(invoice: ApiInvoiceRow) {
  const width = window.screen.availWidth;
  const height = window.screen.availHeight;
  const previewWindow = window.open('', '_blank', `popup=yes,width=${width},height=${height},left=0,top=0`);

  if (!previewWindow) {
    window.alert('Przeglądarka zablokowała okno podglądu faktury. Zezwól na wyskakujące okna dla xKsef.');
    return;
  }

  previewWindow.document.title = 'Podgląd faktury';
  previewWindow.document.body.style.cssText = 'margin:0;font-family:Segoe UI,sans-serif;background:#f3f6fa;';

  const container = previewWindow.document.createElement('div');
  container.style.cssText = 'display:flex;align-items:center;justify-content:center;height:100vh;font-size:18px;color:#435974;';
  container.textContent = 'Trwa pobieranie faktury...';
  previewWindow.document.body.appendChild(container);

const controller = new AbortController();
const context = api.context.capture('tenant');
let pdfUrl: string | null = null;

previewManager.register(previewWindow, controller, () => {
  if (pdfUrl) URL.revokeObjectURL(pdfUrl);
});

  void invoiceClient.getInvoicePdf(invoice.ksefNumber, controller.signal).then(pdf => {
    api.context.assertCurrent(context);
    if (previewWindow.closed) return;

   pdfUrl = URL.createObjectURL(pdf);
    const iframe = previewWindow.document.createElement('iframe');
  iframe.src = pdfUrl;
    iframe.title = 'Faktura PDF';
    iframe.style.cssText = 'width:100%;height:100vh;border:0;display:block;';

    previewWindow.document.body.replaceChildren(iframe);

  }).catch((error: unknown) => {
    if (previewWindow.closed) return;

    const message = error instanceof StaleContextError || error instanceof Error && error.name === 'AbortError'
      ? 'Pobieranie anulowano z powodu zmiany kontekstu.'
      : `Nie udało się pobrać faktury. ${errorMessage(error)}`;

    container.textContent = message;
    previewWindow.document.body.replaceChildren(container);
  });
}

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
      {result.items.length > 0 && <InvoiceTable key={`${result.key}:${tableRevision}`} invoices={result.items} direction={direction === 'Sales' ? 'sales' : 'purchase'} onReset={() => setTableRevision(value => value + 1)} onFilteredRowsChange={onFilteredRowsChange} onPreviewInvoice={previewInvoice} />}
      <InvoiceSummaries invoices={filtered?.key === result.key ? filtered.items : result.items} />
    </>}
  </main>;
}
