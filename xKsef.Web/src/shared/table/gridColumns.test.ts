import { describe, expect, it } from 'vitest';
import { compareCalendarDay, compareTableValues, toGridColumns } from './gridColumns';
import type { TableColumn } from './TableColumn';

describe('Business-independent table adapter', () => {
  it('accepts a non-invoice model without importing invoice fields', () => {
    interface LedgerRow { account: string; balance: number | null }
    const columns: TableColumn<LedgerRow>[] = [{ id: 'account', header: 'Konto', kind: 'text', value: row => row.account },
      { id: 'balance', header: 'Saldo', kind: 'number', value: row => row.balance }];
    const result = toGridColumns(columns);
    expect(result.map(column => column.colId)).toEqual(['account', 'balance']);
    expect(result[0].filter).toBe('agTextColumnFilter'); expect(result[1].filter).toBe('agNumberColumnFilter');
    expect(result[1].type).toBe('numericColumn'); expect(result[1].cellDataType).toBe(false);
  });
  it('sorts amounts numerically and preserves the difference between zero and null', () => {
    expect(compareTableValues(2, 10)).toBeLessThan(0); expect(compareTableValues(-50, 0)).toBeLessThan(0);
    expect(compareTableValues(null, 0)).not.toBe(0); expect(compareTableValues(null, null)).toBe(0);
  });
  it('uses Polish alphabetical ordering and natural numeric ordering for text', () => {
    expect(compareTableValues('Łódź', 'Warszawa')).toBeLessThan(0); expect(compareTableValues('FV/2', 'FV/10')).toBeLessThan(0);
  });
  it('sorts offset timestamps by the instant instead of the original text', () => {
    expect(compareTableValues(new Date('2026-11-14T09:00:00+02:00'), new Date('2026-11-14T08:30:00+01:00'))).toBeLessThan(0);
  });
  it('compares calendar days without including the invoice timestamp hour', () => {
    expect(compareCalendarDay(new Date(2026, 10, 14), new Date(2026, 10, 14, 23, 59))).toBe(0);
    expect(compareCalendarDay(new Date(2026, 10, 14), new Date(2026, 10, 15))).toBe(1);
  });
});
