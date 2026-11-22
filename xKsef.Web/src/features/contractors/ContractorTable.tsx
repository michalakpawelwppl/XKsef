import { useMemo, useState } from 'react';
import { DataTable } from '../../shared/table/DataTable';
import { formatCount } from '../../shared/formatting/polish';
import { contractorColumns } from './contractorColumns';
import type { ContractorDto } from './ContractorApiContracts';

const rowId = (contractor: ContractorDto) => contractor.id;

interface Props {
  readonly contractors: readonly ContractorDto[];
  readonly onReset: () => void;
  readonly onEdit: (contractor: ContractorDto) => void;
  readonly busy: boolean;
}

export function ContractorTable({ contractors, onReset, onEdit, busy }: Props) {
  const columns = useMemo(() => contractorColumns(), []);
  const [search, setSearch] = useState('');
  const [visibleCount, setVisibleCount] = useState(contractors.length);
  const [selected, setSelected] = useState<readonly ContractorDto[]>([]);
  const selectedContractors = contractors.filter(row => selected.some(item => item.id === row.id));

  return (
    <section className="invoice-panel" aria-label="Lista kontrahentów">
      <div className="table-toolbar">
        <label className="search-label">Szukaj kontrahenta
          <input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Nazwa, NIP, REGON…" />
        </label>
        <button className="secondary-button" onClick={onReset}>Resetuj widok</button>
        <button className="secondary-button" type="button" disabled={busy || selectedContractors.length !== 1} onClick={() => onEdit(selectedContractors[0])}>Edytuj</button>
        <div className="table-counts" role="status" aria-live="polite">
          <span>Widoczne: <strong>{formatCount(visibleCount)}</strong> / {formatCount(contractors.length)}</span>
          <span>Zaznaczone: <strong>{formatCount(selectedContractors.length)}</strong></span>
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
        onRowDoubleClick={row => { if (!busy) onEdit(row); }}
        rowDoubleClickTitle="Kliknij dwukrotnie, aby edytować kontrahenta."
      />

      <div className="table-footer">
        <span>Strzałki i Tab: nawigacja · Spacja: wybór wiersza · Nagłówek: sortowanie</span>
        <span>Dwukrotne kliknięcie kontrahenta — edycja danych.</span>
      </div>
    </section>
  );
}
