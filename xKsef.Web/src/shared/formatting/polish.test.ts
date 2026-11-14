import { describe, expect, it } from 'vitest';
import { formatAmount, formatCalendarDate, formatLocalTimestamp, parseCalendarDate, parseOffsetTimestamp, parsePolishNumber } from './polish';

describe('Polish presentation', () => {
  it('distinguishes a missing amount and a real zero', () => {
    expect(formatAmount(null)).toBe(''); expect(formatAmount(undefined)).toBe(''); expect(formatAmount(0)).toBe('0,00');
  });
  it('formats positive and corrective amounts with two decimals', () => {
    expect(formatAmount(1234.56)).toMatch(/^1\s234,56$/); expect(formatAmount(-250)).toBe('-250,00'); expect(formatAmount(1.2)).toBe('1,20');
    expect(formatAmount(Number.NaN)).toBe(''); expect(formatAmount(Infinity)).toBe('');
  });
  it('keeps a calendar date as the same local day', () => {
    expect(formatCalendarDate(parseCalendarDate('2026-11-14'))).toBe('14.11.2026');
    expect(formatCalendarDate(parseCalendarDate('0099-01-02'))).toBe('02.01.99');
    expect(parseCalendarDate('2026-02-30')).toBeNull(); expect(parseCalendarDate('14.11.2026')).toBeNull();
    expect(formatCalendarDate(null)).toBe('');
  });
  it('converts instants to the requested zone without modifying the source offset', () => {
    const source = '2026-11-14T08:30:00+01:00'; const parsed = parseOffsetTimestamp(source)!; const before = parsed.getTime();
    expect(formatLocalTimestamp(parsed, 'Europe/Warsaw')).toBe('14.11.2026 08:30');
    expect(formatLocalTimestamp(parsed, 'America/New_York')).toBe('14.11.2026 02:30');
    expect(parsed.getTime()).toBe(before); expect(source).toBe('2026-11-14T08:30:00+01:00');
  });
  it('handles daylight saving time using the actual instant', () => {
    expect(formatLocalTimestamp(parseOffsetTimestamp('2026-07-14T08:30:00Z'), 'Europe/Warsaw')).toBe('14.07.2026 10:30');
    expect(formatLocalTimestamp(parseOffsetTimestamp('2026-11-14T08:30:00Z'), 'Europe/Warsaw')).toBe('14.11.2026 09:30');
  });
  it('does not invent a timestamp when metadata or offset is missing', () => {
    expect(parseOffsetTimestamp(null)).toBeNull(); expect(parseOffsetTimestamp('2026-11-14')).toBeNull();
    expect(parseOffsetTimestamp('2026-11-14T08:30:00')).toBeNull(); expect(formatLocalTimestamp(null)).toBe('');
  });
  it('accepts comma decimal separators in numeric filters without interpreting blanks as zero', () => {
    expect(parsePolishNumber('1 234,56')).toBe(1234.56); expect(parsePolishNumber('-57,50')).toBe(-57.5);
    expect(parsePolishNumber('0')).toBe(0); expect(parsePolishNumber('')).toBeNull(); expect(parsePolishNumber(null)).toBeNull();
    expect(parsePolishNumber('1,2,3')).toBeNull(); expect(parsePolishNumber('abc')).toBeNull();
  });
});
