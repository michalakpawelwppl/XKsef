import { describe, expect, it } from 'vitest';
import { summarizeInvoices } from './invoiceSummaryCalculation';
import type { SummaryInvoice } from './invoiceSummaryCalculation';
import { formatInvoiceAmount, parseInvoiceAmount } from './invoiceMoney';

const row = (gross: string, net: string | null = gross, vat: string | null = '0.00', currency = 'PLN'): SummaryInvoice =>
  ({ currency, grossAmountExact: gross, netAmountExact: net, vatAmountExact: vat });

describe('exact invoice summaries', () => {
  it('sums every row in a currency with exact decimal arithmetic', () => {
    expect(summarizeInvoices([row('0.10'), row('0.20')])).toEqual([
      { currency: 'PLN', invoiceCount: 2, netTotal: 30n, vatTotal: 0n, grossTotal: 30n, missingNetCount: 0, missingVatCount: 0 },
    ]);
  });
  it('separates currencies and preserves negative correction amounts', () => {
    const summaries = summarizeInvoices([row('123.00', '100.00', '23.00'), row('-12.30', '-10.00', '-2.30'), row('0.01', '0.01', '0.00', 'EUR')]);
    expect(summaries.map(item => item.currency)).toEqual(['EUR', 'PLN']);
    expect(summaries[1]).toMatchObject({ invoiceCount: 2, netTotal: 9000n, vatTotal: 2070n, grossTotal: 11070n });
  });
  it('keeps zero amounts distinct from incomplete totals and counts missing values separately', () => {
    expect(summarizeInvoices([row('0.00')])[0]).toMatchObject({ netTotal: 0n, vatTotal: 0n, grossTotal: 0n });
    expect(summarizeInvoices([row('123.00', null, '23.00'), row('0.00', '0.00', null)])[0]).toMatchObject({
      netTotal: null, vatTotal: null, grossTotal: 12300n, missingNetCount: 1, missingVatCount: 1,
    });
  });
  it('does not lose cents beyond Number.MAX_SAFE_INTEGER, including the SQL decimal maximum', () => {
    const summary = summarizeInvoices([row('9999999999999999.99'), row('0.01'), row('-0.02')])[0];
    expect(summary.grossTotal).toBe(999999999999999998n);
    expect(formatInvoiceAmount(summary.grossTotal).replace(/\s/g, '')).toBe('9999999999999999,98');
  });
  it('handles large sums without number overflow or VAT-in-PLN conversion', () => {
    const invoices = Array.from({ length: 10000 }, () => ({ ...row('9999999999999999.99'), vatAmountPlnExact: '999.99' }));
    expect(summarizeInvoices(invoices)[0]).toMatchObject({ invoiceCount: 10000, grossTotal: 9999999999999999990000n, vatTotal: 0n });
  });
  it('does not invent a currency or zero totals for an empty set', () => {
    expect(summarizeInvoices([])).toEqual([]); expect(formatInvoiceAmount(null)).toBe('Brak pełnych danych');
    expect(formatInvoiceAmount(-1n)).toBe('-0,01'); expect(formatInvoiceAmount(0n)).toBe('0,00');
  });
  it.each(['0.1', '1.234', '1,23', '1e3', '', 'NaN', ' 1.00', '01.00'])('rejects malformed exact amounts: %s', value => {
    expect(() => parseInvoiceAmount(value)).toThrow();
  });
});
