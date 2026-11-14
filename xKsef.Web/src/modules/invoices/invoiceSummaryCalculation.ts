import type { InvoiceDto } from './InvoiceApiContracts';
import { parseInvoiceAmount } from './invoiceMoney';

export interface InvoiceSummary {
  readonly currency: string;
  readonly invoiceCount: number;
  readonly netTotal: bigint | null;
  readonly vatTotal: bigint | null;
  readonly grossTotal: bigint;
  readonly missingNetCount: number;
  readonly missingVatCount: number;
}
export type SummaryInvoice = Pick<InvoiceDto, 'currency' | 'netAmountExact' | 'vatAmountExact' | 'grossAmountExact'>;

export function summarizeInvoices(items: readonly SummaryInvoice[]): readonly InvoiceSummary[] {
  const groups = new Map<string, { count: number; net: bigint; vat: bigint; gross: bigint; missingNet: number; missingVat: number }>();
  for (const item of items) {
    const group = groups.get(item.currency) ?? { count: 0, net: 0n, vat: 0n, gross: 0n, missingNet: 0, missingVat: 0 };
    group.count++;
    group.gross += parseInvoiceAmount(item.grossAmountExact);
    if (item.netAmountExact === null) group.missingNet++; else group.net += parseInvoiceAmount(item.netAmountExact);
    if (item.vatAmountExact === null) group.missingVat++; else group.vat += parseInvoiceAmount(item.vatAmountExact);
    groups.set(item.currency, group);
  }
  return Array.from(groups, ([currency, group]) => ({ currency, invoiceCount: group.count,
    netTotal: group.missingNet === 0 ? group.net : null, vatTotal: group.missingVat === 0 ? group.vat : null,
    grossTotal: group.gross, missingNetCount: group.missingNet, missingVatCount: group.missingVat,
  })).sort((a, b) => a.currency.localeCompare(b.currency));
}
