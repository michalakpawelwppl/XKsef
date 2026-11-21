import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AgGridReact } from 'ag-grid-react';
import { AG_GRID_LOCALE_PL_PL } from '@ag-grid-community/locale';
import {
  CellStyleModule, ClientSideRowModelModule, ClientSideRowModelApiModule, DateFilterModule, LocaleModule,
  BigIntFilterModule, NumberFilterModule, QuickFilterModule, RenderApiModule, RowApiModule, RowSelectionModule,
  TextFilterModule, TooltipModule, ValidationModule, themeQuartz,
} from 'ag-grid-community';
import type { ColDef, GetRowIdParams, RowSelectionOptions } from 'ag-grid-community';
import type { TableColumn } from './TableColumn';
import { toGridColumns } from './gridColumns';

// Only the Community modules used by this prototype. No export module is registered.
const modules = [ClientSideRowModelModule, ClientSideRowModelApiModule, RenderApiModule, RowApiModule, RowSelectionModule, TextFilterModule,
  BigIntFilterModule, NumberFilterModule, DateFilterModule, QuickFilterModule, LocaleModule, CellStyleModule, TooltipModule,
  ...(import.meta.env.DEV ? [ValidationModule] : [])];
const locale = { ...AG_GRID_LOCALE_PL_PL, noMatchingRows: 'Brak danych spełniających warunki', bigintFilter: 'Filtr kwot', invalidBigInt: 'Podaj poprawną kwotę z dokładnością do grosza.' };
const theme = themeQuartz.withParams({
  accentColor: '#245ca0', backgroundColor: '#ffffff', foregroundColor: '#21334b',
  headerBackgroundColor: '#f4f7fb', borderColor: '#dce4ed',
  fontFamily: 'Segoe UI, system-ui, sans-serif', fontSize: 13, spacing: 5,
});
const defaults: ColDef = { sortable: true, resizable: true, floatingFilter: true,
  editable: false, wrapHeaderText: true, autoHeaderHeight: true };
const selection: RowSelectionOptions = { mode: 'multiRow', checkboxes: true, headerCheckbox: true,
  selectAll: 'filtered', enableClickSelection: true };

export interface DataTableProps<T> {
  readonly rows: readonly T[];
  readonly columns: readonly TableColumn<T>[];
  readonly getRowId: (row: T) => string;
  readonly label: string;
  readonly searchText?: string;
  readonly onSelectionChange?: (rows: readonly T[]) => void;
  readonly onVisibleCountChange?: (count: number) => void;
  readonly onFilteredRowsChange?: (rows: readonly T[]) => void;
    readonly onResetFilters?: () => void;
    readonly onRowDoubleClick?: (row: T) => void;
}

export function DataTable<T>({ rows, columns, getRowId, label, searchText = '', onSelectionChange, onVisibleCountChange, onFilteredRowsChange, onResetFilters, onRowDoubleClick }: DataTableProps<T>) {
  const resolveRowId = useCallback((params: GetRowIdParams<T>) => getRowId(params.data), [getRowId]);
  const rowData = useMemo(() => Array.from(rows), [rows]);
  const columnDefs = useMemo(() => toGridColumns(columns), [columns]);
  const activeCell = useRef<string | null>(null);
  const gridContainerRef = useRef<HTMLDivElement>(null);
  const [hasActiveFilters, setHasActiveFilters] = useState(false);
  const [resetFilterCell, setResetFilterCell] = useState<HTMLElement | null>(null);

useEffect(() => {
  const container = gridContainerRef.current;
  if (!container) return;

  const selector = '.ag-floating-filter[col-id="ag-Grid-SelectionColumn"] .ag-floating-filter-body';

  const findCell = () => {
    const cell = container.querySelector<HTMLElement>(selector);
    setResetFilterCell(previous => previous === cell ? previous : cell);
  };

  const observer = new MutationObserver(findCell);
  observer.observe(container, { childList: true, subtree: true });

  findCell();

  return () => {
    observer.disconnect();
    setResetFilterCell(null);
  };
}, []);

  const getCopyText = useCallback((columnId: string, value: unknown): string => {
  const column = columns.find(item => item.id === columnId);
  const text = value === null || value === undefined ? '' : column?.format ? column.format(value) : String(value);
  return column?.kind === 'money' ? text.replace(/[\s\u00A0\u202F]/g, '') : text;
}, [columns]);
  return (
    <div ref={gridContainerRef} className="data-table" aria-label={label} onKeyDown={event => {
  if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'c') return;
  if (activeCell.current === null) return;
  if (window.getSelection()?.toString()) return;

  event.preventDefault();
  void navigator.clipboard.writeText(activeCell.current);
}}>
      <AgGridReact<T>
        modules={modules}
        theme={theme}
        rowData={rowData}
        columnDefs={columnDefs}
        defaultColDef={defaults}
        localeText={locale}
        onGridReady={event => event.api.setGridAriaProperty('label', label)}
        rowModelType="clientSide"
        domLayout="normal"
        rowHeight={38}
        headerHeight={50}
        floatingFiltersHeight={38}
        rowBuffer={6}
        animateRows={false}
        ensureDomOrder
        enableCellTextSelection
        onCellMouseOver={event => {
  if (!onRowDoubleClick) return;
  const cell = event.event?.target;
  if (!(cell instanceof HTMLElement)) return;
  const cellElement = cell.closest<HTMLElement>('.ag-cell');
  if (cellElement) cellElement.title = 'Kliknij dwukrotnie, aby wyświetlić podgląd faktury.';
}}

onCellClicked={event => {
  activeCell.current = getCopyText(event.colDef.colId ?? '', event.value);
}}
onRowDoubleClicked={event => {
  if (event.data) onRowDoubleClick?.(event.data);
}}
        getRowId={resolveRowId}
        quickFilterText={searchText}
        cacheQuickFilter
        rowSelection={selection}
        selectionColumnDef={{ width: 46, minWidth: 46, maxWidth: 46, resizable: false, pinned: 'left' }}
        onSelectionChanged={event => onSelectionChange?.(event.api.getSelectedRows())}
        onModelUpdated={event => {
          onVisibleCountChange?.(event.api.getDisplayedRowCount());
          if (onFilteredRowsChange) {
            const filtered: T[] = [];
            event.api.forEachNodeAfterFilter(node => { if (node.data) filtered.push(node.data); });
            onFilteredRowsChange(filtered);
          }
        }}
        onFilterChanged={event => setHasActiveFilters(Object.keys(event.api.getFilterModel()).length > 0)}
        overlayNoRowsTemplate="<span>Brak danych spełniających warunki</span>"
      />
      {resetFilterCell && createPortal(
  <button
    type="button"
    className="ag-filter-reset-button"
    disabled={!hasActiveFilters}
    title="Przywróć domyślne filtry"
    aria-label="Przywróć domyślne filtry"
    onClick={onResetFilters}
  >
    <img src="/icons/filter-reset.svg" alt="" />
  </button>,
  resetFilterCell
)}
    </div>
  );
}
