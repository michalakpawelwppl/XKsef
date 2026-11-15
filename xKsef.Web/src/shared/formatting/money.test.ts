import { describe, expect, it } from 'vitest';
import { formatMoney, formatMoneyCell, moneyToDecimal, parseMoney, parsePolishMoney } from './money';
import { formatInvoiceAmount, parseInvoiceAmount } from '../../modules/invoices/invoiceMoney';

describe('shared exact money', () => {
  it('reuses exactly the same parser and formatter for table cells and invoice summaries', () => {
    expect(parseInvoiceAmount).toBe(parseMoney); expect(formatInvoiceAmount).toBe(formatMoney);
  });
  it.each([
    ['9999999999999999,99', 999999999999999999n], ['-9\u00a0999\u00a0999\u00a0999\u00a0999\u00a0999,98', -999999999999999998n],
    ['1 234,56', 123456n], ['001,2', 120n], [',01', 1n], ['-,01', -1n], ['.1', 10n], ['1.', 100n],
    ['0', 0n], ['-0,00', 0n], ['1,2300', 123n], ['999999999999999999999999.99', 99999999999999999999999999n],
  ] as const)('parses Polish filter input %s without binary arithmetic', (input, expected) => {
    expect(parsePolishMoney(input)).toBe(expected);
  });
  it.each([null, '', ' ', '-', '.', '1,234', '-0.001', '1,2,3', 'abc', 'NaN', 'Infinity', '1e3'])('does not round or invent an amount for invalid input %s', input => {
    expect(parsePolishMoney(input)).toBeNull();
  });
  it.each(['9999999999999999.99', '-9999999999999999.98', '-0.01', '0.00', '1234.56'])('round-trips exact amount %s through Polish display and filter input', text => {
    const cents = parseMoney(text);
    expect(moneyToDecimal(cents)).toBe(text);
    expect(formatMoney(cents).replace(/\s/g, '')).toBe(text.replace('.', ','));
    expect(parsePolishMoney(formatMoney(cents))).toBe(cents);
  });
  it('keeps missing table cells blank and retains the existing incomplete-summary text', () => {
    expect(formatMoneyCell(null)).toBe(''); expect(formatMoneyCell(undefined)).toBe(''); expect(formatMoneyCell(123)).toBe('');
    expect(formatMoneyCell(0n)).toBe('0,00'); expect(formatMoney(null)).toBe('Brak pełnych danych');
    expect(formatMoneyCell(123456n)).toMatch(/^1\s234,56$/);
  });
});
