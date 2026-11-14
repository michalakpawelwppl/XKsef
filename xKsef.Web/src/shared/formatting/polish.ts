const amountFormatter = new Intl.NumberFormat('pl-PL', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: true });
const countFormatter = new Intl.NumberFormat('pl-PL', { useGrouping: true });
const calendarFormatter = new Intl.DateTimeFormat('pl-PL', { year: 'numeric', month: '2-digit', day: '2-digit' });
const instantFormatters = new Map<string | undefined, { date: Intl.DateTimeFormat; time: Intl.DateTimeFormat }>();

export function formatAmount(value: unknown): string {
  return typeof value === 'number' && Number.isFinite(value) ? amountFormatter.format(value) : '';
}

export function formatCount(value: number): string { return countFormatter.format(value); }

// A DateOnly is a calendar date, not UTC midnight. Construct a separate local Date for grid filtering.
export function parseCalendarDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const [, year, month, day] = match.map(Number);
  const date = new Date(0);
  date.setFullYear(year, month - 1, day);
  date.setHours(0, 0, 0, 0);
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day ? date : null;
}

export function parseOffsetTimestamp(value: string | null): Date | null {
  if (!value || !/T.*(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return null;
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? date : null;
}

export function formatCalendarDate(value: unknown): string {
  return value instanceof Date && Number.isFinite(value.getTime()) ? calendarFormatter.format(value) : '';
}

export function formatLocalTimestamp(value: unknown, timeZone?: string): string {
  if (!(value instanceof Date) || !Number.isFinite(value.getTime())) return '';
  let formats = instantFormatters.get(timeZone);
  if (!formats) {
    formats = {
      date: new Intl.DateTimeFormat('pl-PL', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone }),
      time: new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }),
    };
    instantFormatters.set(timeZone, formats);
  }
  return `${formats.date.format(value)} ${formats.time.format(value)}`;
}

export function parsePolishNumber(value: string | null): number | null {
  if (value === null || value.trim() === '') return null;
  const normalized = value.replace(/\s/g, '').replace(',', '.');
  if (!/^-?(?:\d+(?:\.\d*)?|\.\d+)$/.test(normalized)) return null;
  const number = Number(normalized);
  return Number.isFinite(number) ? number : null;
}
