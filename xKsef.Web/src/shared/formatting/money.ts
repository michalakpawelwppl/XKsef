/** Exact two-decimal amounts represented as integer minor units. */
export function parseMoney(value: string): bigint {
  if (!/^-?(?:0|[1-9]\d*)\.\d{2}$/.test(value)) throw new Error('Nieprawidłowa dokładna kwota faktury.');
  const negative = value.startsWith('-');
  const [whole, fraction] = (negative ? value.slice(1) : value).split('.');
  const units = BigInt(whole) * 100n + BigInt(fraction);
  return negative ? -units : units;
}

export function moneyToDecimal(value: bigint): string {
  const magnitude = value < 0n ? -value : value;
  return `${value < 0n ? '-' : ''}${magnitude / 100n}.${(magnitude % 100n).toString().padStart(2, '0')}`;
}

export function formatMoney(value: bigint | null): string {
  if (value === null) return 'Brak pełnych danych';
  const magnitude = value < 0n ? -value : value;
  return `${value < 0n ? '-' : ''}${(magnitude / 100n).toLocaleString('pl-PL', { useGrouping: true })},${(magnitude % 100n).toString().padStart(2, '0')}`;
}

export function formatMoneyCell(value: unknown): string { return typeof value === 'bigint' ? formatMoney(value) : ''; }

/** Normalize Polish filter input before using the same parser as invoice summaries. Never round input. */
export function parsePolishMoney(value: string | null): bigint | null {
  if (value === null) return null;
  const normalized = value.replace(/\s/g, '').replace(',', '.');
  const match = /^(-?)(\d*)(?:\.(\d*))?$/.exec(normalized);
  if (!match || !match[2] && !match[3]) return null;
  const fraction = match[3] ?? '';
  if (/[^0]/.test(fraction.slice(2))) return null;
  return parseMoney(`${match[1]}${BigInt(match[2] || '0')}.${fraction.slice(0, 2).padEnd(2, '0')}`);
}
