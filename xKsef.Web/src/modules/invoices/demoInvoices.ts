import type { Invoice, InvoiceDirection } from './Invoice';

const parties = ['Alfa i Synowie sp. z o.o.', 'Pracownia Żółty Tulipan', 'Łódzkie Centrum Usług', 'Przedsiębiorstwo Handlowo-Usługowe Północ i Południe sp. z o.o.'];
const currencies = ['PLN', 'EUR', 'USD'];

// Deterministic synthetic data only. No API, KSeF, database or user credentials are accessed.
export function createDemoInvoices(count: number, direction: InvoiceDirection): readonly Invoice[] {
  if (!Number.isInteger(count) || count < 0 || count > 100_000) throw new RangeError('Expected between 0 and 100,000 demo rows.');
  return Array.from({ length: count }, (_, index): Invoice => {
    const number = String(index + 1).padStart(6, '0');
    const issueDate = new Date(Date.UTC(2026, 10, 14 - index % 31)).toISOString().slice(0, 10);
    const correction = index === 1 || index > 3 && index % 17 === 0;
    const netCents = index === 0 ? 123456 : index === 1 ? -25000 : index === 2 ? 0 : (correction ? -1 : 1) * (10000 + index * 37 % 900000);
    const vatCents = Math.round(netCents * 23 / 100);
    const missing = index === 3 || index > 3 && index % 23 === 0;
    return {
      id: `demo-${direction}-${number}`,
      direction,
      invoiceNumber: `${correction ? 'KOR' : 'FV'}/2026/${number}`,
      ksefNumber: `9999999999-${issueDate.replaceAll('-', '')}-${String(index + 1).padStart(12, '0')}-DE`,
      sellerNip: index === 0 ? '0987654321' : String(9000000000 + index % 1000000),
      sellerName: parties[(index + 1) % parties.length],
      buyerNip: index === 3 ? null : index === 0 ? '0123456789' : String(8000000000 + index % 1000000),
      buyerName: index === 3 ? null : parties[index % parties.length],
      issueDate,
      invoicingDate: missing ? null : `${issueDate}T08:30:00+01:00`,
      currency: index < 4 ? 'PLN' : currencies[index % currencies.length],
      netAmount: missing ? null : netCents / 100,
      vatAmount: missing ? null : vatCents / 100,
      grossAmount: (netCents + vatCents) / 100,
    };
  });
}
