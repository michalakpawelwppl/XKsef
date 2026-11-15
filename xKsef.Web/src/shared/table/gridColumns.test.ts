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
  it('sorts BigInt cents in both directions, including adjacent large amounts, corrections, zero and null', () => {
    const low = 999999999999999998n; const high = low + 1n;
    const values = [high, 0n, low, null, -low, -high];
    expect([...values].sort(compareTableValues)).toEqual([-high, -low, 0n, low, high, null]);
    expect([...values].sort((a, b) => -compareTableValues(a, b))).toEqual([null, high, low, 0n, -low, -high]);
    expect(compareTableValues(low, low)).toBe(0);
  });
  it('uses the native BigInt filter with inverse Polish minor-unit input and display functions', () => {
    const [column] = toGridColumns([{ id: 'amount', header: 'Kwota', kind: 'money', value: () => 999999999999999999n }]);
    expect(column.filter).toBe('agBigIntColumnFilter'); expect(column.type).toBe('numericColumn');
    const params = column.filterParams as { bigintParser(value: string): bigint; bigintFormatter(value: bigint | null): string | null; maxNumConditions: number };
    const cents = params.bigintParser('9 999 999 999 999 999,99');
    expect(cents).toBe(999999999999999999n);
    expect(params.bigintFormatter(cents)?.replace(/\s/g, '')).toBe('9999999999999999,99');
    expect(params.bigintFormatter(null)).toBeNull(); expect(params.maxNumConditions).toBe(1);
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
