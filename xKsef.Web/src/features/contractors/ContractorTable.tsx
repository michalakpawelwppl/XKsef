import { useMemo, useState } from 'react';
import { DataTable } from '../../shared/table/DataTable';
import { formatCount } from '../../shared/formatting/polish';
import { contractorColumns } from './contractorColumns';
import type { ContractorDto } from './ContractorApiContracts';

const rowId = (contractor: ContractorDto) => contractor.id;

interface Props {
  readonly contractors: readonly ContractorDto[];
  readonly onReset: () => void;
}

export function ContractorTable({ contractors, onReset }: Props) {
  const columns = useMemo(() => contractorColumns(), []);
  const [search, setSearch] = useState('');
  const [visibleCount, setVisibleCount] = useState(contractors.length);
  const [selected, setSelected] = useState<readonly ContractorDto[]>([]);

  return (
    <section className="invoice-panel" aria-label="Lista kontrahentów">
      <div className="table-toolbar">
        <label className="search-label">Szukaj kontrahenta
          <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Nazwa, NIP, REGON…" />
        </label>
        <button className="secondary-button" onClick={onReset}>Resetuj widok</button>
        <div className="table-counts" role="status" aria-live="polite">
          <span>Widoczne: <strong>{formatCount(visibleCount)}</strong> / {formatCount(contractors.length)}</span>
          <span>Zaznaczone: <strong>{formatCount(selected.length)}</strong></span>
        </div>
      </div>

      {contractors.length > 0 && visibleCount === 0 && <p role="status">Brak kontrahentów spełniających ustawione filtry.</p>}

      <DataTable<ContractorDto>
        rows={contractors}
        columns={columns}
        getRowId={rowId}
        label="Kontrahenci"
        searchText={search}
        onSelectionChange={setSelected}
        onVisibleCountChange={setVisibleCount}
        onResetFilters={onReset}
      />

      <div className="table-footer">
        <span>Strzałki i Tab: nawigacja · Spacja: wybór wiersza · Nagłówek: sortowanie</span>
        <span>Dwukrotne kliknięcie kontrahenta — funkcja edycji zostanie dodana później.</span>
      </div>
    </section>
  );
}