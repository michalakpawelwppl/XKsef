import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { test as base, loginUser, tenantA, tenantB } from '../fixtures/sharedBrowserSession';
import type { ContractorDto, GusContractorDto } from '../../src/features/contractors/ContractorApiContracts';

const id = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
function entity(fullName = 'Firma GUS'): GusContractorDto {
  return { fullName, nip: '8567346215', regon: '123456785', krs: null, shortName: null,
    address: { voivodeship: null, county: null, municipality: null, city: 'Warszawa', street: null, buildingNumber: '7', apartmentNumber: null, postalCode: '00-001', postOfficeCity: 'Inna poczta' } };
}
function stored(): ContractorDto {
  return { id, fullName: 'Stara firma', shortName: 'KEEP', nip: '8567346215', regon: '123456785', krsNumber: '0000123456',
    isActive: false, createdAt: '2026-11-22T12:00:00Z', euVatNumber: 'PL123', otherTaxNumber: 'OTHER', isEuVatRegistered: true,
    addresses: [{ addressType: 1, countryCode: 'DE', city: 'Berlin', street: 'Stara', buildingNumber: '2', apartmentNumber: '3', postalCode: '12345' }] };
}
function gate() {
  let arrive!: () => void; let release!: () => void;
  return { arrived: new Promise<void>(resolve => { arrive = resolve; }), released: new Promise<void>(resolve => { release = resolve; }), arrive: () => arrive(), release: () => release() };
}
interface Registry {
  results: GusContractorDto[]; status: number | 'network'; urls: string[]; writes: string[];
  pending: ReturnType<typeof gate> | null;
}
const test = base.extend<{ registry: Registry }>({
  registry: async ({ page, sessionApi }, use) => {
    sessionApi.setTenants('user-A', [{ id: tenantA, name: 'Firma A', role: 'User' }, { id: tenantB, name: 'Firma B', role: 'User' }]);
    const registry: Registry = { results: [entity()], status: 200, urls: [], writes: [], pending: null };
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/api/contractors**', async route => {
      const request = route.request(); const path = new URL(request.url()).pathname;
      expect((await request.allHeaders())['x-tenant-id']).toBeTruthy();
      if (request.method() !== 'GET') {
        registry.writes.push(request.method());
        return request.method() === 'POST' ? route.fulfill({ status: 201, json: { id } }) : route.fulfill({ status: 204 });
      }
      if (path === '/api/contractors/gus') {
        registry.urls.push(request.url());
        const results = structuredClone(registry.results); const status = registry.status;
        const pending = registry.pending; registry.pending = null;
        if (pending) { pending.arrive(); await pending.released; }
        if (status === 'network') return route.abort('failed');
        return route.fulfill({ status, headers: { 'Retry-After': '7' }, json: status === 200 ? results : { title: 'INTERNAL SECRET MUST NOT BE DISPLAYED' } });
      }
      return route.fulfill({ json: path === '/api/contractors' ? [stored()] : stored() });
    });
    await use(registry);
    expect(errors).toEqual([]);
  },
});
const form = (page: Page) => page.getByRole('dialog', { name: /^(Nowy kontrahent|Edycja kontrahenta)$/, includeHidden: true });
const name = (page: Page) => form(page).getByLabel('Nazwa pełna *', { exact: true });
const searchDialog = (page: Page) => page.getByRole('dialog', { name: 'Pobierz dane z GUS', exact: true });
const download = (page: Page) => searchDialog(page).getByRole('button', { name: 'Pobierz', exact: true });
const lookup = (page: Page) => form(page).getByRole('button', { name: 'Pobierz dane z GUS' });
async function open(page: Page, edit = false) {
  await page.goto('/login'); await loginUser(page, 'user-A'); await page.goto('/app/contractors');
  if (edit) await page.locator(`[row-id="${id}"] [col-id="fullName"]`).dblclick();
  else await page.getByRole('button', { name: 'Nowy kontrahent', exact: true }).click();
  await expect(name(page)).toHaveValue(edit ? 'Stara firma' : '');
}
async function search(page: Page) {
  await lookup(page).click();
  await searchDialog(page).getByLabel('NIP', { exact: true }).fill('8567346215');
  await download(page).click();
}
async function verifySession(page: Page) {
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' }); document.dispatchEvent(new Event('visibilitychange'));
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' }); document.dispatchEvent(new Event('visibilitychange'));
  });
}

async function ignoreGusAbort(page: Page) {
  await page.addInitScript(() => {
    const nativeFetch = window.fetch.bind(window);
    window.fetch = (input, init) => String(input).startsWith('/api/contractors/gus?')
      ? nativeFetch(input, { ...init, signal: undefined }) : nativeFetch(input, init);
  });
}

test('opens with three empty enabled fields; typing locks others and clearing unlocks them', async ({ page, registry }) => {
  await open(page, true); await expect(form(page).getByRole('combobox')).toHaveCount(0); await lookup(page).click();
  for (const label of ['NIP', 'REGON', 'KRS']) {
    await expect(searchDialog(page).getByLabel(label, { exact: true })).toBeEnabled();
    await expect(searchDialog(page).getByLabel(label, { exact: true })).toHaveValue('');
  }
  await expect(download(page)).toBeDisabled();
  for (const label of ['NIP', 'REGON', 'KRS']) {
    await searchDialog(page).getByLabel(label, { exact: true }).fill('1');
    for (const other of ['NIP', 'REGON', 'KRS'].filter(value => value !== label)) await expect(searchDialog(page).getByLabel(other, { exact: true })).toBeDisabled();
    await expect(download(page)).toBeEnabled(); await expect(searchDialog(page).getByRole('alert')).toHaveCount(0);
    await searchDialog(page).getByLabel(label, { exact: true }).fill('');
    for (const other of ['NIP', 'REGON', 'KRS']) await expect(searchDialog(page).getByLabel(other, { exact: true })).toBeEnabled();
  }
  await searchDialog(page).getByRole('button', { name: 'Anuluj' }).click();
  await expect(name(page)).toHaveValue('Stara firma'); expect(registry.urls).toEqual([]); expect(registry.writes).toEqual([]);
  await lookup(page).click();
  for (const label of ['NIP', 'REGON', 'KRS']) await expect(searchDialog(page).getByLabel(label, { exact: true })).toHaveValue('');
});

for (const [label, invalid, valid, key] of [['NIP', '8567346214', '856-734-62-15', 'nip'], ['REGON', '123456784', '123456785', 'regon'], ['KRS', '000012345a', '0000123456', 'krs']]) test(`validates ${label} and sends exactly that identifier`, async ({ page, registry }) => {
  await open(page, true); await lookup(page).click();
  const input = searchDialog(page).getByLabel(label, { exact: true });
  await input.fill(invalid); await expect(download(page)).toBeEnabled(); await expect(searchDialog(page).getByRole('alert')).toHaveCount(0);
  await download(page).click();
  await expect(searchDialog(page).getByRole('alert')).toBeVisible();
  await expect(input).toHaveAttribute('aria-invalid', 'true');
  await expect(input).toHaveAttribute('aria-describedby', `gus-search-validation-${key}`);
  expect(registry.urls).toHaveLength(0);
  await input.fill('1'); await expect(searchDialog(page).getByRole('alert')).toHaveCount(0);
  await expect(input).toHaveAttribute('aria-invalid', 'false');
  expect(registry.urls).toHaveLength(0);
  await download(page).click(); await expect(searchDialog(page).getByRole('alert')).toBeVisible();
  expect(registry.urls).toHaveLength(0);
  await input.fill(''); await expect(searchDialog(page).getByRole('alert')).toHaveCount(0); await expect(download(page)).toBeDisabled();
  await input.fill(valid); await expect(searchDialog(page).getByRole('alert')).toHaveCount(0); await expect(download(page)).toBeEnabled(); await download(page).click();
  await expect(searchDialog(page)).toHaveCount(0); await expect(name(page)).toHaveValue('Firma GUS');
  expect([...new URL(registry.urls[0]).searchParams]).toEqual([[key, valid]]); expect(registry.writes).toEqual([]);
});

for (const edit of [false, true]) test(`one result updates the form without auto-saving (edit=${edit})`, async ({ page, registry }) => {
  await open(page, edit); await search(page);
  await expect(name(page)).toHaveValue('Firma GUS');
  await expect(form(page).getByLabel('Ulica', { exact: true })).toHaveValue('');
  await expect(form(page).getByLabel('Numer lokalu')).toHaveValue('');
  await expect(form(page).getByLabel('Miejscowość', { exact: true })).toHaveValue('Warszawa');
  await expect(form(page).getByLabel('Kraj', { exact: true })).toHaveValue('PL');
  if (edit) {
    await expect(form(page).getByLabel('Nazwa skrócona *', { exact: true })).toHaveValue('KEEP');
    await expect(form(page).getByLabel('KRS', { exact: true })).toHaveValue('0000123456');
    await expect(form(page).getByLabel('Kontrahent aktywny', { exact: true })).not.toBeChecked();
    await expect(form(page).getByLabel('Numer VAT UE', { exact: true })).toHaveValue('PL123');
  }
  expect(registry.writes).toEqual([]);
  await form(page).getByLabel('Nazwa skrócona *', { exact: true }).fill('USER');
  await form(page).getByRole('button', { name: 'Zapisz', exact: true }).click();
  await expect(form(page)).toHaveCount(0); expect(registry.writes).toEqual([edit ? 'PUT' : 'POST']);
});

test('empty results show an informational modal without changing the draft', async ({ page, registry }) => {
  registry.results = []; await open(page, true); await search(page);
  const info = page.getByRole('dialog', { name: 'Nie znaleziono kontrahenta', exact: true });
  await expect(searchDialog(page)).toHaveCount(0);
  await expect(info).toContainText('W rejestrze GUS REGON nie znaleziono podmiotu dla podanego identyfikatora. Dane w formularzu nie zostały zmienione');
  await info.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(name(page)).toHaveValue('Stara firma'); expect(registry.writes).toEqual([]);
});

for (const action of ['select', 'double-click', 'cancel', 'escape'] as const) test(`multiple results: ${action}`, async ({ page, registry }) => {
  registry.results = [entity('Pierwsza firma'), entity('Druga firma')];
  await open(page, true); await search(page);
  const picker = page.getByRole('dialog', { name: 'Wybierz podmiot z GUS', exact: true });
  await expect(searchDialog(page)).toHaveCount(0);
  await expect(picker).toBeVisible(); await expect(picker.getByRole('button', { name: 'Wybierz', exact: true })).toBeDisabled();
  await expect(picker.getByRole('row').nth(2)).toContainText('Warszawa');
  if (action === 'select') {
    await picker.getByLabel('Wybierz Druga firma', { exact: true }).check();
    await picker.getByRole('button', { name: 'Wybierz', exact: true }).click();
  } else if (action === 'double-click') await picker.getByRole('row').filter({ hasText: 'Druga firma' }).dblclick();
  else if (action === 'cancel') await picker.getByRole('button', { name: 'Anuluj', exact: true }).click();
  else await page.keyboard.press('Escape');
  await expect(picker).toHaveCount(0);
  await expect(name(page)).toHaveValue(action === 'cancel' || action === 'escape' ? 'Stara firma' : 'Druga firma');
  expect(registry.writes).toEqual([]);
});

for (const cancel of ['button', 'escape']) test(`cancelling the search with ${cancel} discards its late response and resets fields`, async ({ page, registry }) => {
  await ignoreGusAbort(page); await open(page, true);
  const pending = gate(); registry.pending = pending;
  await search(page); await pending.arrived;
  if (cancel === 'button') await searchDialog(page).getByRole('button', { name: 'Anuluj', exact: true }).click();
  else await page.keyboard.press('Escape');
  await expect(searchDialog(page)).toHaveCount(0); await expect(name(page)).toHaveValue('Stara firma');
  await lookup(page).click();
  for (const label of ['NIP', 'REGON', 'KRS']) {
    await expect(searchDialog(page).getByLabel(label, { exact: true })).toHaveValue('');
    await expect(searchDialog(page).getByLabel(label, { exact: true })).toBeEnabled();
  }
  const late = page.waitForResponse(response => response.url() === registry.urls[0]);
  pending.release(); await (await late).finished();
  await expect(searchDialog(page)).toBeVisible(); await expect(name(page)).toHaveValue('Stara firma');
  expect(registry.writes).toEqual([]); expect(registry.urls).toHaveLength(1);
});

test('API failure keeps the search identifier and permits an explicit retry', async ({ page, registry }) => {
  await open(page, true); registry.status = 429; await search(page);
  await expect(searchDialog(page).getByRole('alert')).toContainText('7 s');
  await expect(searchDialog(page).getByLabel('NIP', { exact: true })).toHaveValue('8567346215');
  await expect(download(page)).toBeEnabled(); await expect(name(page)).toHaveValue('Stara firma');
  registry.status = 200; await download(page).click();
  await expect(searchDialog(page)).toHaveCount(0); await expect(name(page)).toHaveValue('Firma GUS');
  expect(registry.urls).toHaveLength(2); expect(registry.writes).toEqual([]);
});

for (const status of [400, 401, 403, 429, 502, 503, 504, 'network'] as const) test(`handles GUS API error ${status} without changing the draft`, async ({ page, registry }) => {
  registry.status = status; await open(page, true); await search(page);
  if (status === 401 || status === 403) await expect(form(page)).toHaveCount(0);
  else {
    await expect(searchDialog(page).getByRole('alert')).toBeVisible();
    if (status === 429) await expect(searchDialog(page).getByRole('alert')).toContainText('7 s');
    await expect(name(page)).toHaveValue('Stara firma');
  }
  await expect(page.getByText('INTERNAL SECRET MUST NOT BE DISPLAYED', { exact: true })).toHaveCount(0);
  expect(registry.writes).toEqual([]);
});

test('blocks duplicate lookups and saving while waiting', async ({ page, registry }) => {
  await open(page, true); const pending = gate(); registry.pending = pending;
  await search(page); await pending.arrived;
  await expect(searchDialog(page).getByRole('status')).toHaveText('Pobieranie danych z GUS…');
  await expect(download(page)).toBeDisabled(); await expect(form(page).getByRole('button', { name: 'Zapisz', exact: true, includeHidden: true })).toBeDisabled();
  await download(page).evaluate(button => { (button as HTMLButtonElement).click(); (button as HTMLButtonElement).click(); });
  expect(registry.urls).toHaveLength(1); pending.release(); await expect(name(page)).toHaveValue('Firma GUS'); expect(registry.writes).toEqual([]);
});

test('editing while waiting cancels lookup and an older response cannot overwrite a newer result', async ({ page, registry }) => {
  await ignoreGusAbort(page); await open(page, true); const pending = gate(); registry.pending = pending; registry.results = [entity('Stary wynik')];
  await search(page); await pending.arrived;
  // The modal prevents ordinary editing; simulate an external update to verify the revision guard as well.
  await name(page).evaluate(input => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'Ręczna zmiana');
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await expect(form(page).getByRole('alert')).toContainText('Formularz został zmieniony');
  registry.results = [entity('Nowszy wynik')]; await search(page); await expect(name(page)).toHaveValue('Nowszy wynik');
  const late = page.waitForResponse(response => response.url() === registry.urls[0]);
  pending.release(); await (await late).finished();
  await expect(name(page)).toHaveValue('Nowszy wynik'); expect(registry.urls).toHaveLength(2); expect(registry.writes).toEqual([]);
});

for (const change of ['close', 'tenant', 'session', 'permissions'] as const) test(`pending lookup is discarded on ${change}`, async ({ page, registry, sessionApi }) => {
  await ignoreGusAbort(page); await open(page, true); const pending = gate(); registry.pending = pending;
  await search(page); await pending.arrived;
  if (change === 'close') await form(page).getByRole('button', { name: 'Anuluj', exact: true, includeHidden: true }).evaluate(button => (button as HTMLButtonElement).click());
  else if (change === 'tenant') {
    await page.getByLabel('Aktywny tenant').selectOption(tenantB, { force: true }); await expect(form(page)).toHaveCount(0);
    await page.getByLabel('Aktywny tenant').selectOption(tenantA);
  } else {
    if (change === 'session') sessionApi.revokeAll();
    else sessionApi.setTenants('user-A', [{ id: tenantA, name: 'Firma A', role: 'Admin' }]);
    await verifySession(page);
  }
  await expect(form(page)).toHaveCount(0);
  const late = page.waitForResponse(response => response.url() === registry.urls[0]);
  pending.release(); await (await late).finished();
  if (change === 'close' || change === 'tenant') {
    await page.locator(`[row-id="${id}"] [col-id="fullName"]`).dblclick(); await expect(name(page)).toHaveValue('Stara firma');
  }
  expect(registry.writes).toEqual([]);
});

for (const status of [401, 403, 502]) test(`late GUS error ${status} cannot invalidate tenant A after A → B → A`, async ({ page, registry }) => {
  await ignoreGusAbort(page); await open(page, true); const pending = gate(); registry.pending = pending; registry.status = status;
  await search(page); await pending.arrived;
  await page.getByLabel('Aktywny tenant').selectOption(tenantB, { force: true }); await expect(form(page)).toHaveCount(0);
  await page.getByLabel('Aktywny tenant').selectOption(tenantA);
  await page.locator(`[row-id="${id}"] [col-id="fullName"]`).dblclick(); await expect(name(page)).toHaveValue('Stara firma');
  const late = page.waitForResponse(response => response.url() === registry.urls[0]);
  pending.release(); await (await late).finished();
  await expect(name(page)).toHaveValue('Stara firma'); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(tenantA);
  await expect(form(page).getByRole('alert')).toHaveCount(0); expect(registry.writes).toEqual([]);
});
