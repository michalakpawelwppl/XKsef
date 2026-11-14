import { useMemo } from 'react';
import { formatCount } from '../../shared/formatting/polish';
import { formatInvoiceAmount } from './invoiceMoney';
import { summarizeInvoices } from './invoiceSummaryCalculation';
import type { SummaryInvoice } from './invoiceSummaryCalculation';

export function InvoiceSummaries({ invoices }: { readonly invoices: readonly SummaryInvoice[] }) {
  const summaries = useMemo(() => summarizeInvoices(invoices), [invoices]);
  return <section className="invoice-panel invoice-summaries" aria-label="Podsumowanie przefiltrowanych faktur" aria-live="polite">
    <p>Liczba faktur: <strong data-testid="summary-count">{formatCount(invoices.length)}</strong></p>
    {summaries.length === 0 ? <p>Brak faktur do podsumowania.</p> : <>
      <table><thead><tr><th scope="col">Waluta</th><th scope="col">Liczba faktur</th><th scope="col">Netto</th><th scope="col">VAT</th><th scope="col">Brutto</th><th scope="col">Brakujące wartości</th></tr></thead>
        <tbody>{summaries.map(row => <tr key={row.currency} data-currency={row.currency}>
          <th scope="row">{row.currency || 'Nieokreślona'}</th><td>{formatCount(row.invoiceCount)}</td>
          <td data-testid="summary-net">{formatInvoiceAmount(row.netTotal)}</td><td data-testid="summary-vat">{formatInvoiceAmount(row.vatTotal)}</td>
          <td data-testid="summary-gross">{formatInvoiceAmount(row.grossTotal)}</td>
          <td>Netto: {formatCount(row.missingNetCount)} · VAT: {formatCount(row.missingVatCount)}</td>
        </tr>)}</tbody></table>
      <p>Kwoty w walucie faktury. VAT w PLN nie jest doliczany. Brak pełnych danych oznacza brak co najmniej jednej wartości.</p>
    </>}
  </section>;
}
