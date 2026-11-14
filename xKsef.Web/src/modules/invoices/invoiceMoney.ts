export function parseInvoiceAmount(value: string): bigint {
  if (!/^-?(?:0|[1-9]\d*)\.\d{2}$/.test(value)) throw new Error('Nieprawidłowa dokładna kwota faktury.');
  const negative = value.startsWith('-');
  const [whole, fraction] = (negative ? value.slice(1) : value).split('.');
  const units = BigInt(whole) * 100n + BigInt(fraction);
  return negative ? -units : units;
}

export function formatInvoiceAmount(value: bigint | null): string {
  if (value === null) return 'Brak pełnych danych';
  const magnitude = value < 0n ? -value : value;
  return `${value < 0n ? '-' : ''}${(magnitude / 100n).toLocaleString('pl-PL')},${(magnitude % 100n).toString().padStart(2, '0')}`;
}
