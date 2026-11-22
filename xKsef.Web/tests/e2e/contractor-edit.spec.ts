import { expect } from '@playwright/test';
import type { Page, Request } from '@playwright/test';
import { test as base, loginUser, tenantA, tenantB } from '../fixtures/sharedBrowserSession';
import type { ContractorDto, ContractorRequest } from '../../src/features/contractors/ContractorApiContracts';

const id = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const secondId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
function contractor(): ContractorDto {
  return { id, fullName: 'Firma z listy', shortName: 'FIR', isActive: false, createdAt: '2026-11-22T12:00:00Z',
    nip: '8567346215', regon: '123456785', krsNumber: '0000123456', euVatNumber: 'PL8567346215', otherTaxNumber: 'OTHER', isEuVatRegistered: true,
    addresses: [{ addressType: 1, countryCode: 'PL', city: 'Poznań', street: 'Kwiatowa', buildingNumber: '12', apartmentNumber: '3', postalCode: '60-001' }] };
}
function gate() {
  let arrive!: () => void; let release!: () => void;
  return { arrived: new Promise<void>(resolve => { arrive = resolve; }), released: new Promise<void>(resolve => { release = resolve; }), arrive: () => arrive(), release: () => release() };
}
interface Catalog {
  dto: ContractorDto;
  requests: Request[];
  saves: ContractorRequest[];
  creates: ContractorRequest[];
  listStatus: number;
  getStatus: number;
  putStatus: number | 'network';
  validation: boolean;
  holdGet: ReturnType<typeof gate> | null;
  holdPut: ReturnType<typeof gate> | null;
}
const test = base.extend<{ catalog: Catalog }>({
  catalog: async ({ page, sessionApi }, use) => {
    sessionApi.setTenants('user-A', [{ id: tenantA, name: 'Firma A', role: 'User' }, { id: tenantB, name: 'Firma B', role: 'User' }]);
    const catalog: Catalog = { dto: contractor(), requests: [], saves: [], creates: [], listStatus: 200, getStatus: 200, putStatus: 204, validation: false, holdGet: null, holdPut: null };
    const browserErrors: string[] = [];
    page.on('pageerror', error => browserErrors.push(error.message));
    await page.route('**/api/contractors**', async route => {
      const request = route.request(); catalog.requests.push(request);
      const path = new URL(request.url()).pathname;
      const headers = await request.allHeaders();
      expect([tenantA, tenantB]).toContain(headers['x-tenant-id']); expect(headers.authorization).toBeUndefined();
      if (path === '/api/contractors') {
        if (request.method() === 'POST') {
          expect(headers['x-csrf-token']).toBe('fixture-token');
          catalog.creates.push(request.postDataJSON() as ContractorRequest);
          return route.fulfill({ status: 201, json: { id: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee' } });
        }
        expect(request.method()).toBe('GET');
        if (catalog.listStatus !== 200) return route.fulfill({ status: catalog.listStatus, json: {} });
        // Distinct tenant data makes unintended restoration of tenant A observable.
        const rows = headers['x-tenant-id'] === tenantA
          ? [catalog.dto, { ...catalog.dto, id: secondId, fullName: 'Druga firma' }, ...catalog.creates.map(body => ({ ...body, id: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', createdAt: catalog.dto.createdAt }))]
          : [{ ...catalog.dto, id: 'ffffffff-ffff-ffff-ffff-ffffffffffff', fullName: 'Kontrahent firmy B' }];
        return route.fulfill({ json: rows });
      }
      if (request.method() === 'GET') {
        const dto = structuredClone(catalog.dto); const status = catalog.getStatus;
        const pending = catalog.holdGet; catalog.holdGet = null;
        if (pending) { pending.arrive(); await pending.released; }
        return route.fulfill({ status, json: status === 200 ? dto : {} });
      }
      expect(request.method()).toBe('PUT'); expect(headers['x-csrf-token']).toBe('fixture-token');
      const body = request.postDataJSON() as ContractorRequest; catalog.saves.push(body);
      const pending = catalog.holdPut; catalog.holdPut = null;
      if (pending) { pending.arrive(); await pending.released; }
      if (catalog.putStatus === 'network') return route.abort('failed');
      if (catalog.putStatus === 204) { Object.assign(catalog.dto, body); return route.fulfill({ status: 204 }); }
      return route.fulfill({ status: catalog.putStatus, json: catalog.validation ? { errors: { FullName: ['Nazwa jest nieprawidłowa.'] } } : {} });
    });
    await use(catalog);
    expect(browserErrors, 'Uncaught browser errors').toEqual([]);
  },
});
const dialog = (page: Page) => page.getByRole('dialog', { name: 'Edycja kontrahenta', exact: true });
const name = (page: Page) => dialog(page).getByLabel('Nazwa pełna *', { exact: true });
const cell = (page: Page) => page.locator(`[row-id="${id}"] [col-id="fullName"]`);
async function openPage(page: Page) { await page.goto('/login'); await loginUser(page, 'user-A'); await page.goto('/app/contractors'); await expect(cell(page)).toBeVisible(); }
async function openEdit(page: Page) { await cell(page).dblclick(); await expect(name(page)).toHaveValue('Firma z listy'); }
async function verifySession(page: Page) {
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' }); document.dispatchEvent(new Event('visibilitychange'));
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' }); document.dispatchEvent(new Event('visibilitychange'));
  });
}

test('opens by double click, loads fresh data and saves all fields while preserving filters and grid settings', async ({ page, catalog }) => {
  await openPage(page);
  const search = page.getByLabel('Szukaj kontrahenta'); await search.fill('Firma');
  const filter = page.locator('.ag-floating-filter[col-id="shortName"] input'); await filter.fill('FIR');
  const header = page.locator('[role="columnheader"][col-id="fullName"]'); await header.click();
  await page.locator('.data-table').evaluate(node => { (window as unknown as { originalGrid: Element }).originalGrid = node; });
  catalog.dto.fullName = 'Aktualne dane API';
  await cell(page).hover(); await expect(cell(page)).toHaveAttribute('title', 'Kliknij dwukrotnie, aby edytować kontrahenta.');
  await cell(page).dblclick(); await expect(name(page)).toHaveValue('Aktualne dane API');
  await expect(dialog(page).getByLabel('NIP', { exact: true })).toHaveValue('8567346215');
  await expect(dialog(page).getByLabel('Zarejestrowany jako podatnik VAT UE')).toBeChecked();
  await expect(dialog(page).getByLabel('Numer lokalu')).toHaveValue('3');
  await expect(dialog(page).getByLabel('Kontrahent aktywny', { exact: true })).not.toBeChecked();
  await name(page).fill('Firma zmieniona');
  await dialog(page).getByLabel('Nazwa skrócona *', { exact: true }).fill('FIR2');
  await dialog(page).getByLabel('REGON', { exact: true }).fill('12345678512347');
  await dialog(page).getByLabel('KRS', { exact: true }).fill('0000654321');
  await dialog(page).getByLabel('Numer VAT UE', { exact: true }).fill('PL123');
  await dialog(page).getByLabel('Inny numer identyfikacji podatkowej').fill('OTHER2');
  for (const [label, value] of [['Kraj', 'DE'], ['Kod pocztowy', '12345'], ['Miejscowość', 'Berlin'], ['Ulica', 'Nowa'], ['Numer budynku', '9'], ['Numer lokalu', '4']]) await dialog(page).getByLabel(label, { exact: true }).fill(value);
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(dialog(page)).toHaveCount(0); await expect(cell(page).locator('.copy-cell-text')).toHaveText('Firma zmieniona');
  expect(catalog.saves).toEqual([{ fullName: 'Firma zmieniona', shortName: 'FIR2', nip: '8567346215', regon: '12345678512347', krsNumber: '0000654321', euVatNumber: 'PL123', otherTaxNumber: 'OTHER2', isEuVatRegistered: true, isActive: false,
    addresses: [{ addressType: 1, countryCode: 'DE', city: 'Berlin', street: 'Nowa', buildingNumber: '9', apartmentNumber: '4', postalCode: '12345' }] }]);
  await expect(search).toHaveValue('Firma'); await expect(filter).toHaveValue('FIR'); await expect(header).toHaveAttribute('aria-sort', 'ascending');
  expect(await page.locator('.data-table').evaluate(node => node === (window as unknown as { originalGrid: Element }).originalGrid)).toBe(true);
});

test('enables Edit only for exactly one selection and cancellation never sends PUT', async ({ page, catalog }) => {
  await openPage(page); const edit = page.getByRole('button', { name: 'Edytuj', exact: true }); await expect(edit).toBeDisabled();
  const first = page.locator(`[row-id="${id}"] input[type="checkbox"]`); const second = page.locator(`[row-id="${secondId}"] input[type="checkbox"]`);
  await first.check(); await expect(edit).toBeEnabled(); await second.check(); await expect(edit).toBeDisabled(); await second.uncheck(); await edit.click();
  await expect(name(page)).toHaveValue('Firma z listy'); await name(page).fill('Niezapisana');
  await dialog(page).getByLabel('Kontrahent aktywny', { exact: true }).check();
  await dialog(page).getByRole('button', { name: 'Anuluj' }).click(); await expect(dialog(page)).toHaveCount(0); expect(catalog.saves).toEqual([]);
  await openEdit(page); await expect(dialog(page).getByLabel('Kontrahent aktywny', { exact: true })).not.toBeChecked();
  await page.keyboard.press('Escape'); await expect(dialog(page)).toHaveCount(0); expect(catalog.saves).toEqual([]);
});

for (const addAddress of [false, true]) test(`handles missing address (fill address=${addAddress}) without changing activation`, async ({ page, catalog }) => {
  catalog.dto.addresses = []; catalog.dto.isActive = true; await openPage(page); await openEdit(page);
  await expect(dialog(page).getByLabel('Kraj', { exact: true })).toHaveValue('');
  if (addAddress) await dialog(page).getByLabel('Miejscowość', { exact: true }).fill('Warszawa');
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); await expect(dialog(page)).toHaveCount(0);
  expect(catalog.saves[0].isActive).toBe(true);
  expect(catalog.saves[0].addresses).toEqual(addAddress ? [{ addressType: 1, countryCode: 'PL', city: 'Warszawa', street: '', buildingNumber: '', apartmentNumber: null, postalCode: '' }] : []);
});

test('shows loading, prevents duplicate submissions and disables fields during PUT', async ({ page, catalog }) => {
  await openPage(page); const load = gate(); catalog.holdGet = load; await cell(page).dblclick(); await load.arrived;
  await expect(dialog(page).getByRole('status')).toHaveText('Wczytywanie danych kontrahenta…');
  await expect(dialog(page).getByRole('button', { name: 'Zapisz', exact: true })).toBeDisabled(); load.release(); await expect(name(page)).toBeVisible();
  const save = gate(); catalog.holdPut = save;
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).evaluate(button => { (button as HTMLButtonElement).click(); (button as HTMLButtonElement).click(); });
  await save.arrived; await expect(name(page)).toBeDisabled(); await expect(dialog(page).getByRole('button', { name: 'Zapisywanie…' })).toBeDisabled();
  expect(catalog.saves).toHaveLength(1); save.release(); await expect(dialog(page)).toHaveCount(0);
});

test('local validation blocks PUT and keeps the draft', async ({ page, catalog }) => {
  await openPage(page); await openEdit(page); await name(page).fill(' ');
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(dialog(page).getByRole('alert')).toContainText('Podaj pełną nazwę'); expect(catalog.saves).toEqual([]);
});

for (const status of [400, 404, 500, 'network'] as const) test(`PUT failure ${status} retains draft and allows appropriate recovery`, async ({ page, catalog }) => {
  await openPage(page); await openEdit(page); await name(page).fill('Szkic'); catalog.putStatus = status; catalog.validation = status === 400;
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click();
  const message = status === 400 ? 'Nazwa jest nieprawidłowa.' : status === 404 ? 'Kontrahent nie istnieje' : status === 'network' ? 'Nie udało się połączyć' : 'Nie udało się wykonać';
  await expect(dialog(page).getByRole('alert')).toContainText(message); await expect(name(page)).toHaveValue('Szkic'); expect(catalog.saves).toHaveLength(1);
  if (status === 404) await expect(dialog(page).getByRole('button', { name: 'Zapisz', exact: true })).toBeDisabled();
  else { catalog.putStatus = 204; await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); await expect(dialog(page)).toHaveCount(0); expect(catalog.saves).toHaveLength(2); }
});

for (const status of [404, 500]) test(`GET failure ${status} prevents saving and offers retry only when appropriate`, async ({ page, catalog }) => {
  await openPage(page); catalog.getStatus = status; await cell(page).dblclick(); await expect(dialog(page).getByRole('alert')).toBeVisible();
  await expect(dialog(page).getByRole('button', { name: 'Zapisz', exact: true })).toBeDisabled();
  if (status === 404) await expect(dialog(page).getByRole('button', { name: 'Spróbuj ponownie' })).toHaveCount(0);
  else { catalog.getStatus = 200; await dialog(page).getByRole('button', { name: 'Spróbuj ponownie' }).click(); await expect(name(page)).toHaveValue('Firma z listy'); }
  expect(catalog.saves).toEqual([]);
});

for (const phase of ['GET', 'PUT'] as const) test(`tenant A → B → A invalidates pending ${phase} and ignores its late response`, async ({ page, catalog }) => {
  await openPage(page); const pending = gate();
  if (phase === 'GET') { catalog.holdGet = pending; await cell(page).dblclick(); }
  else { await openEdit(page); catalog.holdPut = pending; await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); }
  await pending.arrived;
  await page.getByLabel('Aktywny tenant').selectOption(tenantB); await expect(dialog(page)).toHaveCount(0);
  await page.getByLabel('Aktywny tenant').selectOption(tenantA); pending.release(); await expect(dialog(page)).toHaveCount(0);
  await expect(cell(page)).toBeVisible();
  expect(catalog.saves).toHaveLength(phase === 'PUT' ? 1 : 0);
  for (const request of catalog.requests.filter(r => r.method() === 'PUT')) expect((await request.allHeaders())['x-tenant-id']).toBe(tenantA);
});

for (const phase of ['GET', 'PUT'] as const) test(`logout invalidates pending ${phase}`, async ({ page, catalog }) => {
  await openPage(page); const pending = gate();
  if (phase === 'GET') { catalog.holdGet = pending; await cell(page).dblclick(); }
  else { await openEdit(page); catalog.holdPut = pending; await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); }
  await pending.arrived;
  const other = await page.context().newPage(); await other.goto('/app/invoices/sales');
  await other.getByRole('button', { name: 'Wyloguj', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/); pending.release(); await expect(dialog(page)).toHaveCount(0);
});

test('session verification preserves draft, blocks saving and closes on changed permissions', async ({ page, catalog, sessionApi }) => {
  await openPage(page); await openEdit(page); await name(page).fill('Szkic');
  const verification = sessionApi.holdNextIdentity(page); await verifySession(page); await verification.arrived;
  await expect(dialog(page).getByRole('button', { name: 'Zapisz', exact: true })).toBeDisabled(); await expect(name(page)).toHaveValue('Szkic');
  verification.release(503); const recovery = page.getByRole('dialog', { name: 'Weryfikacja sesji', exact: true }); await expect(recovery).toBeVisible();
  await recovery.getByRole('button', { name: 'Ponów weryfikację sesji' }).click(); await expect(recovery).toHaveCount(0);
  await expect(name(page)).toHaveValue('Szkic'); await expect(dialog(page).getByRole('button', { name: 'Zapisz', exact: true })).toBeEnabled();
  sessionApi.setTenants('user-A', [{ id: tenantA, name: 'Firma A', role: 'Admin' }]); await verifySession(page); await expect(dialog(page)).toHaveCount(0); expect(catalog.saves).toEqual([]);
});

for (const phase of ['GET', 'PUT'] as const) for (const change of ['permissions', 'expired', 'user'] as const) test(`${change} invalidates pending ${phase}`, async ({ page, catalog, sessionApi }) => {
  await openPage(page); const pending = gate();
  if (phase === 'GET') { catalog.holdGet = pending; await cell(page).dblclick(); }
  else { await openEdit(page); catalog.holdPut = pending; await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); }
  await pending.arrived;
  if (change === 'user') {
    const other = await page.context().newPage(); await other.goto('/app/invoices/sales');
    await other.getByRole('button', { name: 'Wyloguj', exact: true }).click(); await loginUser(other, 'user-B');
    await expect(page.locator('.session-user')).toHaveText('user-B');
  } else {
    if (change === 'expired') sessionApi.revokeAll();
    else sessionApi.setTenants('user-A', [{ id: tenantA, name: 'Firma A', role: 'Admin' }]);
    await verifySession(page);
  }
  await expect(dialog(page)).toHaveCount(0); pending.release();
  expect(catalog.saves).toHaveLength(phase === 'PUT' ? 1 : 0);
});

test('cancelling while GET is pending ignores late data and does not PUT', async ({ page, catalog }) => {
  await openPage(page); const pending = gate(); catalog.holdGet = pending; await cell(page).dblclick(); await pending.arrived;
  await dialog(page).getByRole('button', { name: 'Anuluj' }).click(); pending.release();
  await expect(dialog(page)).toHaveCount(0); expect(catalog.saves).toEqual([]);
  await openEdit(page); await expect(name(page)).toHaveValue('Firma z listy');
});

async function ignoreContractorAbort(page: Page) {
  await page.addInitScript(() => {
    const nativeFetch = window.fetch.bind(window);
    const aborted: string[] = [];
    (window as unknown as { contractorAborts: string[] }).contractorAborts = aborted;
    window.fetch = (input, init) => {
      if (String(input).startsWith('/api/contractors/')) {
        init?.signal?.addEventListener('abort', () => aborted.push(init?.method ?? 'GET'));
        return nativeFetch(input, { ...init, signal: undefined });
      }
      return nativeFetch(input, init);
    };
  });
}

for (const method of ['GET', 'PUT'] as const) for (const status of [401, 403]) test(`current ${method} HTTP ${status} invalidates the form without retry`, async ({ page, catalog }) => {
  await openPage(page);
  if (method === 'GET') { catalog.getStatus = status; await cell(page).dblclick(); }
  else { await openEdit(page); catalog.putStatus = status; await name(page).fill('Szkic'); await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); }
  await expect(dialog(page)).toHaveCount(0);
  if (status === 401) await expect(page).toHaveURL(/\/login$/);
  else { await expect(page.getByLabel('Aktywny tenant')).toHaveValue(''); await expect(page.getByText('Wybierz tenanta, aby otworzyć moduł.')).toBeVisible(); }
  expect(catalog.requests.filter(request => new URL(request.url()).pathname === `/api/contractors/${id}` && request.method() === method)).toHaveLength(1);
  expect(catalog.saves).toHaveLength(method === 'PUT' ? 1 : 0);
});

test('changing tenant while editing discards draft and sends no PUT even after returning to A', async ({ page, catalog }) => {
  await openPage(page); await openEdit(page); await name(page).fill('Szkic firmy A');
  await page.getByLabel('Aktywny tenant').selectOption(tenantB);
  await expect(dialog(page)).toHaveCount(0); await expect(cell(page)).toHaveCount(0);
  await expect(page.locator('[row-id="ffffffff-ffff-ffff-ffff-ffffffffffff"] [col-id="fullName"] .copy-cell-text')).toHaveText('Kontrahent firmy B');
  await page.getByLabel('Aktywny tenant').selectOption(tenantA); await openEdit(page);
  await expect(name(page)).toHaveValue('Firma z listy'); expect(catalog.saves).toEqual([]);
});

test('loss of tenant membership closes the edited draft and clears tenant data without PUT', async ({ page, catalog, sessionApi }) => {
  await openPage(page); await openEdit(page); await name(page).fill('Szkic');
  sessionApi.setTenants('user-A', [{ id: tenantB, name: 'Firma B', role: 'User' }]); await verifySession(page);
  await expect(dialog(page)).toHaveCount(0); await expect(page.getByLabel('Aktywny tenant')).toHaveValue('');
  await expect(cell(page)).toHaveCount(0); expect(catalog.saves).toEqual([]);
});

for (const method of ['GET', 'PUT'] as const) for (const status of method === 'GET' ? [200, 401, 403, 500] : [204, 400, 401, 403, 404, 500]) test(`late ${method} HTTP ${status} cannot affect A → B → A even if fetch ignores abort`, async ({ page, catalog }) => {
  await ignoreContractorAbort(page); await openPage(page); const pending = gate();
  if (method === 'GET') { catalog.getStatus = status; catalog.holdGet = pending; await cell(page).dblclick(); }
  else { await openEdit(page); catalog.putStatus = status; catalog.holdPut = pending; await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); }
  await pending.arrived;
  await page.getByLabel('Aktywny tenant').selectOption(tenantB); await expect(dialog(page)).toHaveCount(0);
  await expect(cell(page)).toHaveCount(0); await expect(page.locator('[row-id="ffffffff-ffff-ffff-ffff-ffffffffffff"] [col-id="fullName"] .copy-cell-text')).toHaveText('Kontrahent firmy B');
  await page.getByLabel('Aktywny tenant').selectOption(tenantA); await expect(cell(page)).toBeVisible();
  const reads = catalog.requests.filter(request => new URL(request.url()).pathname === '/api/contractors').length;
  const response = page.waitForResponse(response => new URL(response.url()).pathname === `/api/contractors/${id}` && response.request().method() === method && response.status() === status);
  pending.release(); await (await response).finished();
  await expect(dialog(page)).toHaveCount(0); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(tenantA);
  await expect(page.getByRole('alert')).toHaveCount(0); await expect(page.locator('.session-user')).toHaveText('user-A');
  expect(catalog.requests.filter(request => new URL(request.url()).pathname === '/api/contractors')).toHaveLength(reads);
  expect(await page.evaluate(() => (window as unknown as { contractorAborts: string[] }).contractorAborts)).toContain(method);
  for (const request of catalog.requests.filter(request => request.method() === 'PUT')) expect((await request.allHeaders())['x-tenant-id']).toBe(tenantA);
});

test('closing during PUT aborts the request and a late success cannot close a subsequently opened draft', async ({ page, catalog }) => {
  await ignoreContractorAbort(page); await openPage(page); await openEdit(page); await name(page).fill('Pierwszy zapis');
  const pending = gate(); catalog.holdPut = pending;
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); await pending.arrived;
  await dialog(page).getByRole('button', { name: 'Anuluj' }).click(); await expect(dialog(page)).toHaveCount(0);
  await openEdit(page); await name(page).fill('Nowy niezapisany szkic');
  const response = page.waitForResponse(response => response.request().method() === 'PUT'); pending.release(); await (await response).finished();
  await expect(name(page)).toHaveValue('Nowy niezapisany szkic'); expect(catalog.saves).toHaveLength(1);
  expect(await page.evaluate(() => (window as unknown as { contractorAborts: string[] }).contractorAborts)).toContain('PUT');
});

test('adding a contractor sends a full POST with tenant and CSRF and refreshes the list', async ({ page, catalog }) => {
  await openPage(page); await page.getByRole('button', { name: 'Nowy kontrahent', exact: true }).click();
  const form = page.getByRole('dialog', { name: 'Nowy kontrahent', exact: true });
  await expect(form.getByLabel('Kontrahent aktywny', { exact: true })).toBeChecked();
  await expect(form.getByLabel('Kontrahent aktywny', { exact: true })).toBeDisabled();
  for (const [label, value] of [['Nazwa pełna *', 'Nowa firma'], ['Nazwa skrócona *', 'NOWA'], ['NIP', '8567346215'], ['REGON', '12345678512347'], ['KRS', '0000123456'], ['Numer VAT UE', 'PL8567346215'], ['Inny numer identyfikacji podatkowej', 'OTHER'], ['Kraj', 'PL'], ['Kod pocztowy', '60-001'], ['Miejscowość', 'Poznań'], ['Ulica', 'Kwiatowa'], ['Numer budynku', '12'], ['Numer lokalu', '3']]) await form.getByLabel(label, { exact: true }).fill(value);
  await form.getByLabel('Zarejestrowany jako podatnik VAT UE').check();
  await form.getByRole('button', { name: 'Zapisz', exact: true }).click(); await expect(form).toHaveCount(0);
  await expect(page.locator('[row-id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"] [col-id="fullName"] .copy-cell-text')).toHaveText('Nowa firma');
  const { id: _id, createdAt: _createdAt, ...expected } = contractor();
  expect(catalog.creates).toEqual([{ ...expected, fullName: 'Nowa firma', shortName: 'NOWA', regon: '12345678512347', isActive: true }]);
  expect(catalog.saves).toEqual([]);
  const posted = catalog.requests.filter(request => request.method() === 'POST'); expect(posted).toHaveLength(1);
  expect((await posted[0].allHeaders())['x-tenant-id']).toBe(tenantA);
});

test('a list refresh failure after successful PUT preserves filters and does not repeat the mutation', async ({ page, catalog }) => {
  await openPage(page); await page.getByLabel('Szukaj kontrahenta').fill('Firma'); await openEdit(page);
  await name(page).fill('Firma po zapisie'); catalog.listStatus = 500;
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); await expect(dialog(page)).toHaveCount(0);
  await expect(page.getByRole('alert')).toBeVisible(); expect(catalog.saves).toHaveLength(1);
  await expect(page.getByLabel('Szukaj kontrahenta')).toHaveValue('Firma');
  catalog.listStatus = 200; await page.getByRole('button', { name: 'Spróbuj ponownie', exact: true }).click();
  await expect(cell(page).locator('.copy-cell-text')).toHaveText('Firma po zapisie'); expect(catalog.saves).toHaveLength(1);
});

for (const [initial, target] of [[true, false], [false, true], [true, true], [false, false]]) test(`editing activation ${initial} → ${target} saves the full form and keeps the row visible`, async ({ page, catalog }) => {
  catalog.dto.isActive = initial; await openPage(page); await openEdit(page);
  const active = dialog(page).getByLabel('Kontrahent aktywny', { exact: true });
  await expect(active).toBeChecked({ checked: initial }); await expect(active).toBeEnabled();
  await active.setChecked(target); await expect(active).toBeChecked({ checked: target });
  expect(catalog.saves).toEqual([]); expect(catalog.dto.isActive).toBe(initial);
  await name(page).fill('Firma po edycji');
  await dialog(page).getByRole('button', { name: 'Zapisz', exact: true }).click(); await expect(dialog(page)).toHaveCount(0);
  const { id: _id, createdAt: _createdAt, ...expected } = contractor();
  expect(catalog.saves).toEqual([{ ...expected, fullName: 'Firma po edycji', isActive: target }]);
  await expect(cell(page).locator('.copy-cell-text')).toHaveText('Firma po edycji');
  await expect(page.locator(`[row-id="${id}"] [col-id="isActive"] .copy-cell-text`)).toHaveText(target ? 'Aktywny' : 'Nieaktywny');
});
