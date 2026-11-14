import type { TableColumn } from '../../shared/table/TableColumn';
import { formatAmount, formatCalendarDate, formatLocalTimestamp, parseCalendarDate, parseOffsetTimestamp } from '../../shared/formatting/polish';
import type { Invoice, InvoiceDirection } from './Invoice';

export function invoiceColumns(direction: InvoiceDirection): readonly TableColumn<Invoice>[] {
  const sales = direction === 'sales';
  return [
    { id: 'invoiceNumber', header: 'Numer faktury', kind: 'text', value: row => row.invoiceNumber, width: 165 },
    { id: 'ksefNumber', header: 'Numer KSeF', kind: 'text', value: row => row.ksefNumber, width: 175 },
    { id: 'counterpartyNip', header: sales ? 'NIP nabywcy' : 'NIP sprzedawcy', kind: 'text', value: row => sales ? row.buyerNip : row.sellerNip, width: 130 },
    { id: 'counterpartyName', header: sales ? 'Nazwa nabywcy' : 'Nazwa sprzedawcy', kind: 'text', value: row => sales ? row.buyerName : row.sellerName, width: 210 },
    { id: 'issueDate', header: 'Data wystawienia', kind: 'date', value: row => parseCalendarDate(row.issueDate), format: formatCalendarDate, width: 145 },
    { id: 'invoicingDate', header: 'Data przyjęcia do KSeF', kind: 'date', value: row => parseOffsetTimestamp(row.invoicingDate), format: formatLocalTimestamp, width: 180 },
    { id: 'currency', header: 'Waluta', kind: 'text', value: row => row.currency, width: 90 },
    { id: 'netAmount', header: 'Netto', kind: 'number', value: row => row.netAmount, format: formatAmount, width: 125 },
    { id: 'vatAmount', header: 'VAT', kind: 'number', value: row => row.vatAmount, format: formatAmount, width: 115 },
    { id: 'grossAmount', header: 'Brutto', kind: 'number', value: row => row.grossAmount, format: formatAmount, width: 125 },
  ];
}
