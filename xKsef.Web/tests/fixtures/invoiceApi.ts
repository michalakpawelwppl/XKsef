import type { Route } from '@playwright/test';

export function result(label: string) {
  const items = [123, -12.3, 0].map((grossAmount, index) => ({
    ksefNumber: `KSEF-${index}`, invoiceNumber: `${label}-${index}`, sellerName: 'Sprzedawca', sellerNip: '1234567890',
    buyerName: 'Nabywca', buyerNip: '0987654321', issueDate: `2026-11-0${index + 1}`, invoicingDate: null,
    currency: 'PLN', grossAmount, netAmount: index === 2 ? null : grossAmount, vatAmount: 0, vatAmountPln: 0,
    grossAmountExact: grossAmount.toFixed(2), netAmountExact: index === 2 ? null : grossAmount.toFixed(2), vatAmountExact: '0.00', vatAmountPlnExact: '0.00',
  }));
  return { items, summaries: [{ currency: 'PLN', invoiceCount: 3, netTotal: null as number | null, vatTotal: 0, grossTotal: 110.7, missingNetCount: 1, missingVatCount: 0 }] };
}

export async function send(route: Route, body: unknown, status = 200, headers: Record<string, string> = {}) {
  try { await route.fulfill({ status, contentType: 'application/json', headers, body: status === 204 ? undefined : JSON.stringify(body) }); }
  catch { /* The browser can abort an old request before the mock replies. */ }
}
