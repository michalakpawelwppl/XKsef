import { ApiError } from '../../platform/api/ApiError';
import { parseCalendarDate } from '../../shared/formatting/polish';
import { parseInvoiceAmount } from './invoiceMoney';

export type ApiInvoiceDirection = 'Sales' | 'Purchase';
export type InvoiceDateKind = 'IssueDate' | 'InvoicingDate';
export interface InvoiceQuery {
  readonly from: string;
  readonly to: string;
  readonly direction: ApiInvoiceDirection;
  readonly dateKind: InvoiceDateKind;
  readonly timeZoneId?: string;
}

// Mirrors the JSON contract. Numbers retain the existing API transport precision;
// these values must not be used for financial aggregation with binary arithmetic.
export interface InvoiceDto {
  readonly ksefNumber: string;
  readonly invoiceNumber: string;
  readonly sellerName: string;
  readonly sellerNip: string;
  readonly buyerName: string | null;
  readonly buyerNip: string | null;
  readonly issueDate: string;
  readonly invoicingDate: string | null;
  readonly currency: string;
  readonly grossAmount: number;
  readonly netAmount: number | null;
  readonly vatAmount: number | null;
  readonly vatAmountPln: number | null;
  readonly grossAmountExact: string;
  readonly netAmountExact: string | null;
  readonly vatAmountExact: string | null;
  readonly vatAmountPlnExact: string | null;
}
export interface InvoiceCurrencySummaryDto {
  readonly currency: string;
  readonly invoiceCount: number;
  readonly netTotal: number | null;
  readonly vatTotal: number | null;
  readonly grossTotal: number;
  readonly missingNetCount: number;
  readonly missingVatCount: number;
}
export interface InvoiceQueryResultDto {
  readonly items: readonly InvoiceDto[];
  readonly summaries: readonly InvoiceCurrencySummaryDto[];
}

function invalid(): never { throw new ApiError(502, 'Nieprawidłowa odpowiedź API dotycząca faktur.'); }
function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid();
  return value as Record<string, unknown>;
}
function text(value: unknown): string { return typeof value === 'string' ? value : invalid(); }
function nullableText(value: unknown): string | null { return value === null ? null : text(value); }
function amount(value: unknown): number { return typeof value === 'number' && Number.isFinite(value) ? value : invalid(); }
function nullableAmount(value: unknown): number | null { return value === null ? null : amount(value); }
function exactAmount(value: unknown, numeric: unknown): string | null {
  if (value === null && numeric === null) return null;
  if (typeof value !== 'string' || typeof numeric !== 'number' || !Number.isFinite(numeric)) return invalid();
  try { parseInvoiceAmount(value); } catch { return invalid(); }
  // Consistency with the approximate field only; never use it for arithmetic.
  if (Number(value) !== numeric) return invalid();
  return value;
}
function count(value: unknown): number { const result = amount(value); return Number.isSafeInteger(result) && result >= 0 ? result : invalid(); }
export function isInvoiceDate(value: unknown): value is string {
  return typeof value === 'string' && !value.startsWith('0000-') && parseCalendarDate(value) !== null;
}
export function invoiceDateFilterError(query: Pick<InvoiceQuery, 'from' | 'to' | 'dateKind'>): string | null {
  if (!isInvoiceDate(query.from) || !isInvoiceDate(query.to)) return 'Podaj poprawne daty od i do.';
  if (query.from > query.to) return 'Data od nie może być późniejsza niż data do.';
  if (query.dateKind !== 'IssueDate' && query.dateKind !== 'InvoicingDate') return 'Wybierz poprawny rodzaj daty.';
  if (query.dateKind === 'InvoicingDate' && query.to === '9999-12-31') return 'Dla daty przyjęcia do KSeF data do musi być wcześniejsza niż 31.12.9999.';
  return null;
}
function timestamp(value: unknown): string | null {
  if (value === null) return null;
  const result = text(value);
  const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-](\d{2}):(\d{2}))$/.exec(result);
  if (!match || !isInvoiceDate(match[1]) || +match[2] > 23 || +match[3] > 59 || +match[4] > 59
      || (match[6] !== undefined && (+match[6] > 14 || +match[7] > 59 || +match[6] === 14 && +match[7] !== 0))
      || !Number.isFinite(Date.parse(result))) return invalid();
  return result;
}
function readInvoice(value: unknown): InvoiceDto {
  const row = record(value);
  const ksefNumber = text(row.ksefNumber);
  if (!ksefNumber.trim() || !isInvoiceDate(row.issueDate)) return invalid();
  return {
    ksefNumber, invoiceNumber: text(row.invoiceNumber), sellerName: text(row.sellerName), sellerNip: text(row.sellerNip),
    buyerName: nullableText(row.buyerName), buyerNip: nullableText(row.buyerNip), issueDate: row.issueDate,
    invoicingDate: timestamp(row.invoicingDate), currency: text(row.currency), grossAmount: amount(row.grossAmount),
    netAmount: nullableAmount(row.netAmount), vatAmount: nullableAmount(row.vatAmount), vatAmountPln: nullableAmount(row.vatAmountPln),
    grossAmountExact: exactAmount(row.grossAmountExact, row.grossAmount)!,
    netAmountExact: exactAmount(row.netAmountExact, row.netAmount), vatAmountExact: exactAmount(row.vatAmountExact, row.vatAmount),
    vatAmountPlnExact: exactAmount(row.vatAmountPlnExact, row.vatAmountPln),
  };
}
function readSummary(value: unknown): InvoiceCurrencySummaryDto {
  const row = record(value);
  const result = {
    currency: text(row.currency), invoiceCount: count(row.invoiceCount), netTotal: nullableAmount(row.netTotal),
    vatTotal: nullableAmount(row.vatTotal), grossTotal: amount(row.grossTotal),
    missingNetCount: count(row.missingNetCount), missingVatCount: count(row.missingVatCount),
  };
  if (result.missingNetCount > result.invoiceCount || result.missingVatCount > result.invoiceCount) return invalid();
  return result;
}
export function readInvoiceQueryResult(value: unknown): InvoiceQueryResultDto {
  const result = record(value);
  if (!Array.isArray(result.items) || !Array.isArray(result.summaries)) return invalid();
  const items = result.items.map(readInvoice);
  const summaries = result.summaries.map(readSummary);
  if (new Set(items.map(row => row.ksefNumber)).size !== items.length
      || new Set(summaries.map(row => row.currency)).size !== summaries.length) return invalid();
  return { items, summaries };
}
