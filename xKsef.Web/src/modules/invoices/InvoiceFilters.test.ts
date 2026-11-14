import { describe, expect, it } from 'vitest';
import { invoiceDateFilterError } from './InvoiceApiContracts';

describe('invoice date range validation', () => {
  it.each([
    ['2026-11-14', '2026-11-14'], ['2024-02-29', '2024-02-29'], ['2026-03-28', '2026-03-30'],
    ['2026-10-24', '2026-10-26'], ['2025-12-31', '2026-01-01'],
  ])('accepts inclusive calendar boundaries %s – %s for both date kinds', (from, to) => {
    for (const dateKind of ['IssueDate', 'InvoicingDate'] as const) expect(invoiceDateFilterError({ from, to, dateKind })).toBeNull();
  });
  it.each([
    ['', '2026-11-14'], ['2026-11-14', ''], ['2026-11-15', '2026-11-14'], ['2026-02-29', '2026-03-01'],
    ['2026-04-31', '2026-05-01'], ['0000-01-01', '2026-11-14'], ['2026-1-01', '2026-11-14'],
  ])('rejects invalid calendar boundaries %s – %s', (from, to) => {
    expect(invoiceDateFilterError({ from, to, dateKind: 'IssueDate' })).toEqual(expect.any(String));
  });
  it('preserves the API upper bound difference between issue dates and KSeF timestamps', () => {
    expect(invoiceDateFilterError({ from: '9999-12-31', to: '9999-12-31', dateKind: 'IssueDate' })).toBeNull();
    expect(invoiceDateFilterError({ from: '9999-12-30', to: '9999-12-31', dateKind: 'InvoicingDate' })).toEqual(expect.any(String));
    expect(invoiceDateFilterError({ from: '9999-12-30', to: '9999-12-30', dateKind: 'InvoicingDate' })).toBeNull();
  });
});
