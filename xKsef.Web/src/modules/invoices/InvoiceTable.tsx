import { useMemo, useState } from 'react';
import { DataTable } from '../../shared/table/DataTable';
import { formatCount } from '../../shared/formatting/polish';
import { invoiceColumns } from './invoiceColumns';
import type { Invoice, InvoiceDirection } from './Invoice';

const rowId = (invoice: Invoice) => invoice.id;

interface Props<T extends Invoice> {
  readonly invoices: readonly T[];
  readonly direction: InvoiceDirection;
  readonly onReset: () => void;
  readonly onFilteredRowsChange?: (rows: readonly T[]) => void;
}

export function InvoiceTable<T extends Invoice>({ invoices, direction, onReset, onFilteredRowsChange }: Props<T>) {
  const columns = useMemo(() => invoiceColumns(direction), [direction]);
  const [search, setSearch] = useState('');
  const [visibleCount, setVisibleCount] = useState(invoices.length);
  const [selected, setSelected] = useState<readonly T[]>([]);
  return (
    <section className="invoice-panel" aria-label="Lista faktur">
      <div className="table-toolbar">
        <label className="search-label">Szukaj w fakturach
          <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Numer, kontrahent, NIP…" />
        </label>
        <button className="secondary-button" onClick={onReset}>Resetuj widok</button>
        <div className="table-counts" role="status" aria-live="polite">
          <span>Widoczne: <strong data-testid="visible-count">{formatCount(visibleCount)}</strong> / {formatCount(invoices.length)}</span>
          <span>Zaznaczone: <strong data-testid="selected-count">{formatCount(selected.length)}</strong></span>
        </div>
      </div>
      {invoices.length > 0 && visibleCount === 0 && <p role="status">Brak faktur spełniających ustawione filtry. Zmień filtry lub zresetuj widok.</p>}
      <DataTable<T> rows={invoices} columns={columns} getRowId={rowId} label={direction === 'sales' ? 'Faktury sprzedaży' : 'Faktury zakupu'}
        searchText={search} onSelectionChange={setSelected} onVisibleCountChange={setVisibleCount} onFilteredRowsChange={onFilteredRowsChange} />
      <div className="table-footer">
        <span>Strzałki i Tab: nawigacja · Spacja: wybór wiersza · Nagłówek: sortowanie</span>
        <span>Kwoty w walucie faktury · Puste pole oznacza brak danych</span>
      </div>
    </section>
  );
}
