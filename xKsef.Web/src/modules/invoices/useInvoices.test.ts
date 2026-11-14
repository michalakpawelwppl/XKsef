import { describe, expect, it } from 'vitest';
import { defaultInvoiceQuery } from './useInvoices';

describe('default invoice query', () => {
  it.each(['Sales', 'Purchase'] as const)('requests exactly 30 inclusive local calendar days for %s', direction => {
    const today = new Date(2026, 10, 14, 23, 59);
    expect(defaultInvoiceQuery(direction, today)).toEqual({ from: '2026-10-16', to: '2026-11-14', direction, dateKind: 'IssueDate' });
    expect(today.getDate()).toBe(14);
  });
  it.each([
    [new Date(2026, 0, 10), '2025-12-12', '2026-01-10'],
    [new Date(2024, 2, 1), '2024-02-01', '2024-03-01'],
    [new Date(2026, 3, 5), '2026-03-07', '2026-04-05'],
  ])('handles month, year, leap-day and DST boundaries: %s', (today, from, to) => {
    expect(defaultInvoiceQuery('Sales', today)).toMatchObject({ from, to });
  });
});
