import { describe, expect, it } from 'vitest';
import { createDemoInvoices } from './demoInvoices';
import { invoiceColumns } from './invoiceColumns';
import type { Invoice } from './Invoice';

describe('Invoice-specific table configuration', () => {
  it.each(['sales', 'purchase'] as const)('defines ten ordered business columns for %s', direction => {
    const columns = invoiceColumns(direction);
    expect(columns.map(column => column.id)).toEqual(['invoiceNumber', 'ksefNumber', 'counterpartyNip', 'counterpartyName', 'issueDate', 'invoicingDate', 'currency', 'netAmount', 'vatAmount', 'grossAmount']);
    expect(columns.some(column => column.id === 'vatAmountPln')).toBe(false);
  });
  it('uses buyer fields for sales and seller fields for purchases', () => {
    const row = createDemoInvoices(1, 'sales')[0];
    expect(invoiceColumns('sales')[2].value(row)).toBe('0123456789'); expect(invoiceColumns('sales')[3].value(row)).toBe(row.buyerName);
    expect(invoiceColumns('purchase')[2].value(row)).toBe('0987654321'); expect(invoiceColumns('purchase')[3].value(row)).toBe(row.sellerName);
  });
  it('keeps source invoices immutable, including the original timestamp string', () => {
    const row: Invoice = Object.freeze(createDemoInvoices(1, 'sales')[0]);
    for (const column of invoiceColumns('sales')) column.format?.(column.value(row));
    expect(row.invoicingDate).toBe('2026-11-14T08:30:00+01:00'); expect(row.issueDate).toBe('2026-11-14');
  });
  it.each(['netAmount', 'vatAmount', 'grossAmount'] as const)('uses the exact %s field even when adjacent amounts share the same approximate number', name => {
    const low = '9999999999999999.98'; const high = '9999999999999999.99';
    expect(Number(low)).toBe(Number(high));
    const base = createDemoInvoices(1, 'sales')[0];
    const column = invoiceColumns('sales').find(column => column.id === name)!;
    const first = { ...base, [name]: Number(low), [name + 'Exact']: low };
    const second = { ...base, [name]: Number(high), [name + 'Exact']: high };
    expect(column.kind).toBe('money'); expect(column.value(first)).toBe(999999999999999998n); expect(column.value(second)).toBe(999999999999999999n);
    expect(column.format!(column.value(first)).replace(/\s/g, '')).toBe('9999999999999999,98');
    expect(column.format!(column.value(second)).replace(/\s/g, '')).toBe('9999999999999999,99');
  });
  it('includes corrections, multiple currencies, real zero and missing metadata', () => {
    const rows = createDemoInvoices(10, 'sales');
    expect(rows[1].netAmount).toBeLessThan(0); expect(rows[2].netAmount).toBe(0); expect(rows[3].netAmount).toBeNull(); expect(rows[3].invoicingDate).toBeNull();
    expect(new Set(rows.map(row => row.currency))).toEqual(new Set(['PLN', 'EUR', 'USD']));
  });
  it('creates reproducible large datasets with stable unique row IDs', () => {
    const rows = createDemoInvoices(100_000, 'sales'); expect(rows).toHaveLength(100_000);
    expect(new Set(rows.map(row => row.id)).size).toBe(100_000); expect(createDemoInvoices(10, 'sales')).toEqual(rows.slice(0, 10));
    expect(createDemoInvoices(1, 'purchase')[0].id).not.toBe(rows[0].id);
  });
  it('supports an empty dataset and rejects unsupported sizes', () => {
    expect(createDemoInvoices(0, 'sales')).toEqual([]); expect(() => createDemoInvoices(100_001, 'sales')).toThrow(RangeError);
    expect(() => createDemoInvoices(-1, 'sales')).toThrow(RangeError); expect(() => createDemoInvoices(1.5, 'sales')).toThrow(RangeError);
  });
});
