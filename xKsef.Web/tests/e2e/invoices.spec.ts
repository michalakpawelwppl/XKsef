import { expect, test } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import type { Page } from '@playwright/test';

const cell = (page: Page, row: string, column: string) => page.locator(`[role="row"][row-id="${row}"] [col-id="${column}"]`);
const rowId = (number: number, direction = 'sales') => `demo-${direction}-${String(number).padStart(6, '0')}`;
const count = (page: Page) => page.getByTestId('visible-count');

async function openInvoices(page: Page, size = '1000') {
  await page.goto('/prototype/invoices');
  await page.getByLabel('Liczba faktur').selectOption(size);
  await expect.poll(async () => (await count(page).innerText()).replace(/\s/g, '')).toBe(size);
  await expect(cell(page, rowId(1), 'invoiceNumber')).toHaveText('FV/2026/000001');
}

const browserErrors = new WeakMap<Page, string[]>();

test.beforeEach(async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error' || message.type() === 'warning') errors.push(message.text()); });
  browserErrors.set(page, errors);
});

test.afterEach(async ({ page }) => {
  expect(browserErrors.get(page), 'Browser errors and grid module warnings').toEqual([]);
});

test('pokazuje polskie kolumny i właściwego kontrahenta w obu kierunkach', async ({ page }) => {
  await openInvoices(page);
  await expect(page.getByRole('grid', { name: 'Faktury sprzedaży' })).toBeVisible();
  await expect(cell(page, rowId(1), 'counterpartyNip')).toHaveText('0123456789');
  await expect(page.getByRole('columnheader', { name: 'NIP nabywcy' })).toBeVisible();
  await page.getByRole('button', { name: 'Zakup', exact: true }).click();
  await expect(page.getByRole('grid', { name: 'Faktury zakupu' })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'NIP sprzedawcy' })).toBeVisible();
  await expect(cell(page, rowId(1, 'purchase'), 'counterpartyNip')).toHaveText('0987654321');
  await expect(page.getByTestId('selected-count')).toHaveText('0');
});

test('formatuje kwoty i daty po polsku oraz rozróżnia null i zero', async ({ page }) => {
  await openInvoices(page);
  await expect(cell(page, rowId(1), 'netAmount')).toHaveText(/1\s234,56/);
  await expect(cell(page, rowId(2), 'netAmount')).toHaveText('-250,00');
  await expect(cell(page, rowId(3), 'netAmount')).toHaveText('0,00');
  await expect(cell(page, rowId(4), 'netAmount')).toHaveText('');
  await expect(cell(page, rowId(4), 'vatAmount')).toHaveText('');
  await expect(cell(page, rowId(4), 'invoicingDate')).toHaveText('');
  await expect(cell(page, rowId(1), 'issueDate')).toHaveText('14.11.2026');
  await expect(cell(page, rowId(1), 'invoicingDate')).toHaveText('14.11.2026 08:30');
  await expect(cell(page, rowId(1), 'currency')).toHaveText('PLN');
  expect(await cell(page, rowId(1), 'netAmount').evaluate(element => getComputedStyle(element).textAlign)).toBe('right');
});

test('sortuje kwoty numerycznie, uwzględniając wartości ujemne', async ({ page }) => {
  await openInvoices(page);
  const header = page.locator('[role="columnheader"][col-id="netAmount"]');
  await header.click(); await expect(header).toHaveAttribute('aria-sort', 'ascending');
  const values = await page.locator('[role="row"][row-id] [col-id="netAmount"]').allTextContents();
  const numbers = values.filter(Boolean).map(value => Number(value.replace(/\s/g, '').replace(',', '.')));
  expect(numbers.length).toBeGreaterThan(1); expect(numbers[0]).toBeLessThan(0); expect(numbers[0]).toBeLessThanOrEqual(numbers[1]);
  await header.click(); await expect(header).toHaveAttribute('aria-sort', 'descending');
});

test('wyszukuje fakturę i pokazuje pusty wynik', async ({ page }) => {
  await openInvoices(page);
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('FV/2026/000001');
  await expect(count(page)).toHaveText('1');
  await expect(cell(page, rowId(1), 'invoiceNumber')).toBeVisible();
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('nieistniejąca-faktura');
  await expect(count(page)).toHaveText('0');
  await expect(page.getByText('Brak danych spełniających warunki')).toBeVisible();
});

test('filtr liczbowy przyjmuje polski przecinek dziesiętny', async ({ page }) => {
  await openInvoices(page);
  await page.locator('.ag-floating-filter[col-id="netAmount"] input').fill('1234,56');
  await expect(count(page)).toHaveText('1');
  await expect(cell(page, rowId(1), 'netAmount')).toHaveText(/1\s234,56/);
});

test('filtr daty działa dla dni kalendarzowych', async ({ page }) => {
  await openInvoices(page);
  await page.locator('.ag-floating-filter[col-id="issueDate"] input[type="date"]').fill('2026-11-14');
  await expect(count(page)).toHaveText('33');
  await expect(cell(page, rowId(1), 'issueDate')).toHaveText('14.11.2026');
});

test('wybiera wiersze i zachowuje ich tożsamość po sortowaniu', async ({ page }) => {
  await openInvoices(page);
  for (const number of [1, 2]) await page.locator(`[role="row"][row-id="${rowId(number)}"] input[type="checkbox"]`).check();
  await expect(page.getByTestId('selected-count')).toHaveText('2');
  await page.locator('[role="columnheader"][col-id="invoiceNumber"]').click();
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('FV/2026/000001');
  await expect(count(page)).toHaveText('1'); await expect(page.getByTestId('selected-count')).toHaveText('2');
  await expect(page.locator(`[role="row"][row-id="${rowId(1)}"]`)).toHaveAttribute('aria-selected', 'true');
});

test('zaznaczenie nagłówka obejmuje wyłącznie przefiltrowane wiersze', async ({ page }) => {
  await openInvoices(page);
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('FV/2026/000001'); await expect(count(page)).toHaveText('1');
  await page.locator('.ag-header-select-all input[type="checkbox"]').check();
  await expect(page.getByTestId('selected-count')).toHaveText('1');
});

test('obsługuje strzałki, Tab, Shift+Tab i wybór wiersza spacją', async ({ page }) => {
  await openInvoices(page);
  const first = cell(page, rowId(1), 'invoiceNumber'); await first.focus();
  await page.keyboard.press('ArrowRight'); await expect(cell(page, rowId(1), 'ksefNumber')).toBeFocused();
  await page.keyboard.press('Tab'); await expect(cell(page, rowId(1), 'counterpartyNip')).toBeFocused();
  await page.keyboard.press('Shift+Tab'); await expect(cell(page, rowId(1), 'ksefNumber')).toBeFocused();
  await page.keyboard.press('ArrowDown'); await expect(cell(page, rowId(2), 'ksefNumber')).toBeFocused();
  await page.keyboard.press('Space'); await expect(page.getByTestId('selected-count')).toHaveText('1');
});

test('reset usuwa wyszukiwanie, filtry, sortowanie i zaznaczenie', async ({ page }) => {
  await openInvoices(page);
  await page.locator(`[role="row"][row-id="${rowId(1)}"] input[type="checkbox"]`).check();
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('FV/2026/000001'); await expect(count(page)).toHaveText('1');
  await page.getByRole('button', { name: 'Resetuj widok' }).click();
  await expect(page.getByRole('searchbox', { name: 'Szukaj w fakturach' })).toHaveValue('');
  await expect(count(page)).toHaveText(/1\s000/); await expect(page.getByTestId('selected-count')).toHaveText('0');
});

test('wirtualizuje 100 000 rekordów i pozwala przejść do ostatniej faktury', async ({ page }, testInfo) => {
  const start = Date.now(); await openInvoices(page, '100000');
  const rendered = await page.locator('[role="row"][row-id]').count();
  expect(rendered).toBeGreaterThan(0); expect(rendered).toBeLessThan(100);
  await page.locator('.ag-body-vertical-scroll-viewport').evaluate(element => { element.scrollTop = element.scrollHeight; });
  await expect(cell(page, rowId(100000), 'invoiceNumber')).toHaveText('FV/2026/100000');
  expect(await page.locator('[role="row"][row-id]').count()).toBeLessThan(100);
  const loadAndScrollMs = Date.now() - start;
  const searchStart = Date.now();
  await page.getByRole('searchbox', { name: 'Szukaj w fakturach' }).fill('FV/2026/100000');
  await expect(count(page)).toHaveText('1', { timeout: 15000 });
  await expect(cell(page, rowId(100000), 'invoiceNumber')).toHaveText('FV/2026/100000');
  const output = testInfo.outputPath('virtualization.json');
  await mkdir(dirname(output), { recursive: true });
  await writeFile(output, JSON.stringify({ totalRows: 100000, renderedRows: rendered, loadAndScrollMs, searchMs: Date.now() - searchStart }, null, 2));
  await testInfo.attach('virtualization.json', { path: output, contentType: 'application/json' });
});

test('ma przewijanie poziome na węższym ekranie', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 850 }); await openInvoices(page);
  const viewport = page.locator('.ag-body-horizontal-scroll-viewport');
  expect(await viewport.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('działa bez żądań API, KSeF i zasobów zewnętrznych oraz bez eksportu', async ({ page }) => {
  const requests: string[] = []; page.on('request', request => requests.push(request.url()));
  await openInvoices(page);
  expect(requests.every(url => url.startsWith('http://127.0.0.1:4173/'))).toBe(true);
  await expect(page.getByRole('button', { name: /eksport|export/i })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/invoices-prototype.png', fullPage: true });
});

test.describe('strefa czasowa inna niż polska', () => {
  test.use({ timezoneId: 'America/Los_Angeles' });
  test('data wystawienia nie przesuwa się, a data przyjęcia jest lokalna', async ({ page }) => {
    await openInvoices(page);
    await expect(cell(page, rowId(1), 'issueDate')).toHaveText('14.11.2026');
    await expect(cell(page, rowId(1), 'invoicingDate')).toHaveText('13.11.2026 23:30');
  });
});
