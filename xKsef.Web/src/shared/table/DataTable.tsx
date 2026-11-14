import { useCallback, useMemo } from 'react';
import { AgGridReact } from 'ag-grid-react';
import { AG_GRID_LOCALE_PL_PL } from '@ag-grid-community/locale';
import {
  CellStyleModule, ClientSideRowModelModule, DateFilterModule, LocaleModule,
  NumberFilterModule, QuickFilterModule, RenderApiModule, RowApiModule, RowSelectionModule,
  TextFilterModule, TooltipModule, ValidationModule, themeQuartz,
} from 'ag-grid-community';
import type { ColDef, GetRowIdParams, RowSelectionOptions } from 'ag-grid-community';
import type { TableColumn } from './TableColumn';
import { toGridColumns } from './gridColumns';

// Only the Community modules used by this prototype. No export module is registered.
const modules = [ClientSideRowModelModule, RenderApiModule, RowApiModule, RowSelectionModule, TextFilterModule,
  NumberFilterModule, DateFilterModule, QuickFilterModule, LocaleModule, CellStyleModule, TooltipModule,
  ...(import.meta.env.DEV ? [ValidationModule] : [])];
const locale = { ...AG_GRID_LOCALE_PL_PL, noMatchingRows: 'Brak danych spełniających warunki' };
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
}

export function DataTable<T>({ rows, columns, getRowId, label, searchText = '', onSelectionChange, onVisibleCountChange }: DataTableProps<T>) {
  const resolveRowId = useCallback((params: GetRowIdParams<T>) => getRowId(params.data), [getRowId]);
  const rowData = useMemo(() => Array.from(rows), [rows]);
  const columnDefs = useMemo(() => toGridColumns(columns), [columns]);
  return (
    <div className="data-table" aria-label={label}>
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
        getRowId={resolveRowId}
        quickFilterText={searchText}
        cacheQuickFilter
        rowSelection={selection}
        selectionColumnDef={{ width: 46, minWidth: 46, maxWidth: 46, resizable: false, pinned: 'left' }}
        onSelectionChanged={event => onSelectionChange?.(event.api.getSelectedRows())}
        onModelUpdated={event => onVisibleCountChange?.(event.api.getDisplayedRowCount())}
        overlayNoRowsTemplate="<span>Brak danych spełniających warunki</span>"
      />
    </div>
  );
}
