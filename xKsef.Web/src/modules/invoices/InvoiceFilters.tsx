import { useId, useState } from 'react';
import { invoiceDateFilterError } from './InvoiceApiContracts';
import type { InvoiceDateKind, InvoiceQuery } from './InvoiceApiContracts';
import { defaultInvoiceQuery } from './useInvoices';

export type InvoiceDateFilters = Pick<InvoiceQuery, 'from' | 'to' | 'dateKind'>;
interface Props { readonly query: InvoiceQuery; readonly onApply: (filters: InvoiceDateFilters) => void }

export function InvoiceFilters({ query, onApply }: Props) {
  const [draft, setDraft] = useState<InvoiceDateFilters>({ from: query.from, to: query.to, dateKind: query.dateKind });
  const [error, setError] = useState<string | null>(null);
  const errorId = useId();
  const dateKindId = useId();
  const change = (patch: Partial<InvoiceDateFilters>) => { setDraft(value => ({ ...value, ...patch })); setError(null); };
  function apply() {
    const message = invoiceDateFilterError(draft); setError(message);
    if (!message) onApply(draft);
  }
  function reset() {
    const defaults = defaultInvoiceQuery(query.direction);
    setDraft(defaults); setError(null); onApply(defaults);
  }
  return <form className="invoice-panel invoice-filters" aria-label="Filtrowanie faktur według dat" noValidate onSubmit={event => { event.preventDefault(); apply(); }}>
    <div className="table-toolbar">
      <label className="dataset-label"><span id={dateKindId}>Rodzaj daty</span><select aria-labelledby={dateKindId} value={draft.dateKind} onChange={event => change({ dateKind: event.target.value as InvoiceDateKind })}>
        <option value="IssueDate">Data wystawienia</option><option value="InvoicingDate">Data przyjęcia do KSeF</option>
      </select></label>
      <label className="search-label">Data od<input type="date" required min="0001-01-01" max="9999-12-31" value={draft.from} aria-invalid={Boolean(error)} aria-describedby={error ? errorId : undefined} onChange={event => change({ from: event.target.value })} /></label>
      <label className="search-label">Data do<input type="date" required min="0001-01-01" max={draft.dateKind === 'InvoicingDate' ? '9999-12-30' : '9999-12-31'} value={draft.to} aria-invalid={Boolean(error)} aria-describedby={error ? errorId : undefined} onChange={event => change({ to: event.target.value })} /></label>
      <button className="primary-button" type="submit">Filtruj</button>
      <button className="secondary-button" type="button" onClick={reset}>Przywróć domyślne</button>
      {draft.dateKind === 'InvoicingDate' && <span>Strefa czasowa: Europe/Warsaw</span>}
    </div>
    {error && <p id={errorId} className="error-message" role="alert">{error}</p>}
  </form>;
}
