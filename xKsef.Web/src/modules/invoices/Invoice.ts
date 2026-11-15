export type InvoiceDirection = 'sales' | 'purchase';

// Read-only browser model. Exact fields are authoritative for financial display and comparisons.
export interface Invoice {
  readonly id: string;
  readonly direction: InvoiceDirection;
  readonly invoiceNumber: string;
  readonly ksefNumber: string;
  readonly sellerNip: string;
  readonly sellerName: string;
  readonly buyerNip: string | null;
  readonly buyerName: string | null;
  readonly issueDate: string;
  readonly invoicingDate: string | null;
  readonly currency: string;
  readonly netAmount: number | null;
  readonly vatAmount: number | null;
  readonly grossAmount: number;
  readonly netAmountExact: string | null;
  readonly vatAmountExact: string | null;
  readonly grossAmountExact: string;
}
