import type { ColDef } from 'ag-grid-community';
import { parsePolishNumber } from '../formatting/polish';
import { formatMoney, parsePolishMoney } from '../formatting/money';
import type { TableColumn } from './TableColumn';
import { CopyCellRenderer } from './CopyCellRenderer';


const textOrder = new Intl.Collator('pl-PL', { numeric: true, sensitivity: 'base' });

export function compareTableValues(left: unknown, right: unknown): number {
  if (left === right) return 0;
  if (left === null || left === undefined) return 1;
  if (right === null || right === undefined) return -1;
  if (left instanceof Date && right instanceof Date) return left.getTime() - right.getTime();
  if (typeof left === 'number' && typeof right === 'number') return left - right;
  if (typeof left === 'bigint' && typeof right === 'bigint') return left < right ? -1 : 1;
  return textOrder.compare(String(left), String(right));
}

export function compareCalendarDay(filter: Date, value: Date): number {
  const day = new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime();
  return Math.sign(day - filter.getTime());
}

export function toGridColumns<T>(columns: readonly TableColumn<T>[]): ColDef<T>[] {
  return columns.map(column => {
    const format = column.format ?? ((value: unknown) => value === null || value === undefined ? '' : String(value));
    return {
      colId: column.id,
      headerName: column.header,
      headerTooltip: column.header,
      width: column.width ?? 150,
      minWidth: column.minWidth ?? 90,
      cellDataType: false,
      valueGetter: params => params.data ? column.value(params.data) : null,
      valueFormatter: params => format(params.value),
      cellRenderer: CopyCellRenderer,
cellRendererParams: {
  format,
  isMoney: column.kind === 'money',
},
      tooltipValueGetter: params => format(params.value),
      getQuickFilterText: params => `${format(params.value)} ${format(params.value).replace(/\s/g, '')}`,
      comparator: compareTableValues,
      type: column.kind === 'number' || column.kind === 'money' ? 'numericColumn' : undefined,
      cellClass: column.kind === 'number' || column.kind === 'money' ? ['ag-right-aligned-cell', 'xksef-number'] : undefined,
      filter: column.kind === 'money' ? 'agBigIntColumnFilter' : column.kind === 'number' ? 'agNumberColumnFilter' : column.kind === 'date' ? 'agDateColumnFilter' : 'agTextColumnFilter',
      filterParams: {
        debounceMs: 150,
        maxNumConditions: 1,
        ...(column.kind === 'number' ? { numberParser: parsePolishNumber, allowedCharPattern: '\\d\\-\\s,.' } : {}),
        ...(column.kind === 'money' ? { bigintParser: parsePolishMoney, bigintFormatter: (value: bigint | null) => value === null ? null : formatMoney(value), allowedCharPattern: '\\d\\-\\s,.' } : {}),
        ...(column.kind === 'date' ? { comparator: compareCalendarDay } : {}),
      },
    };
  });
}
