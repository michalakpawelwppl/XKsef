import { expect, test } from '@playwright/test';
import type { Locator, Page, Route } from '@playwright/test';
import { result, send } from '../fixtures/invoiceApi';

const A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
async function mockApi(page: Page, options: { emptyTenants?: boolean; loginRequired?: boolean; query?: (route: Route, index: number) => Promise<void> } = {}) {
  let authenticated = !options.loginRequired; let user = 'user-A';
  const queries: { url: URL; tenant: string | undefined; method: string }[] = [];
  const paths: string[] = [];
  await page.route('**/api/**', async route => {
    const request = route.request(); const url = new URL(request.url()); paths.push(url.pathname);
    expect(request.headers().authorization).toBeUndefined();
    if (url.pathname === '/api/browser-auth/logout') {
      expect(request.method()).toBe('POST'); expect(request.headers()['x-csrf-token']).toBe('fixture-token');
      authenticated = false; return route.fulfill({ status: 204 });
    }
    if (url.pathname === '/api/browser-auth/login' && options.loginRequired) {
      expect(request.method()).toBe('POST'); expect(request.headers()['x-csrf-token']).toBe('fixture-token');
      user = (request.postDataJSON() as { login: string }).login; expect(['user-A', 'user-B']).toContain(user);
      authenticated = true; return send(route, { userId: user, login: user });
    }
    expect(request.method()).toBe('GET');
    if (url.pathname === '/api/browser-auth/me') return send(route, options.loginRequired ? { userId: user, login: user } : { userId: 'user-1', login: 'Testowy użytkownik' }, authenticated ? 200 : 401);
    if (url.pathname === '/api/browser-auth/csrf') return send(route, { requestToken: 'fixture-token', headerName: 'X-CSRF-TOKEN' });
    if (url.pathname === '/api/tenants') return send(route, options.emptyTenants ? [] : options.loginRequired ? [
      { id: user === 'user-A' ? A : B, name: `Firma ${user}`, role: 'User' },
    ] : [
      { id: A, name: 'Firma A', role: 'Admin' }, { id: B, name: 'Firma B', role: 'User' },
    ]);
    expect(url.pathname).toBe('/api/invoices/query');
    const tenant = request.headers()['x-tenant-id']; expect([A, B]).toContain(tenant);
    queries.push({ url, tenant, method: request.method() });
    if (options.query) return options.query(route, queries.length);
    return send(route, result(`${tenant === A ? 'A' : 'B'}-${url.searchParams.get('direction')}`));
  });
  return { queries, paths };
}
const invoiceCell = (page: Page, text: string) => page.locator('[role="gridcell"][col-id="invoiceNumber"]').filter({ has: page.getByText(text, { exact: true }) });
const cellValue = (cell: Locator) => cell.locator('.copy-cell-text');

async function openPurchases(page: Page) {
  const menu = page.getByTitle('Handel', { exact: true });
  await expect(menu).toBeVisible();
  if (await menu.getAttribute('aria-expanded') !== 'true') await menu.click();
  await expect(menu).toHaveAttribute('aria-expanded', 'true');
  await page.getByRole('link', { name: 'Zakup', exact: true }).click();
}

function precisionInvoices() {
  const base = result('BASE').items[0];
  const values = [
    ['HI', '9999999999999999.99'], ['LO', '9999999999999999.98'],
    ['NEG-HI', '-9999999999999999.99'], ['NEG-LO', '-9999999999999999.98'],
    ['ZERO', '0.00'], ['SMALL', '123.45'], ['MISSING', null],
  ] as const;
  return { items: values.map(([label, value]) => ({ ...base, ksefNumber: `KSEF-PREC-${label}`, invoiceNumber: `PREC-${label}`,
    netAmount: value === null ? null : Number(value), vatAmount: value === null ? null : Number(value), grossAmount: Number(value ?? '0.00'),
    netAmountExact: value, vatAmountExact: value, grossAmountExact: value ?? '0.00',
  })), summaries: [{ currency: 'PLN', invoiceCount: 7, netTotal: null, vatTotal: null, grossTotal: 123.45, missingNetCount: 1, missingVatCount: 1 }] };
}

test('untrusted invoice text remains inert in counterparty cells, invoice numbers and tooltips', async ({ page }) => {
  const number = '<img src=x onerror="window.__invoiceXss=1">';
  const party = '<svg onload="window.__invoiceXss=2"></svg><script>window.__invoiceXss=3</script>';
  await page.addInitScript(() => { Object.assign(window, { __invoiceXss: 0 }); });
  const body = result('INERT'); body.items = [{ ...body.items[0], invoiceNumber: number, buyerName: party }];
  await mockApi(page, { query: route => send(route, body) }); await page.goto('/app/invoices/sales');
  for (const [column, text] of [['invoiceNumber', number], ['counterpartyName', party]]) {
    const target = page.locator(`[role="row"][row-id] [role="gridcell"][col-id="${column}"]`);
    await expect(cellValue(target)).toHaveText(text); await expect(target.locator('img,svg,script')).toHaveCount(0);
    await target.hover(); const tooltip = page.getByRole('tooltip');
    await expect(tooltip).toHaveText(text); await expect(tooltip.locator('img,svg,script')).toHaveCount(0);
    await page.getByRole('heading', { name: 'Faktury sprzedaży', exact: true }).hover(); await expect(tooltip).toHaveCount(0);
  }
  expect(await page.evaluate(() => (window as unknown as { __invoiceXss: number }).__invoiceXss)).toBe(0);
});

for (const column of ['netAmount', 'vatAmount', 'grossAmount'] as const) {
  test(`exact ${column} display, sort, filtering, summary membership and native clipboard retain adjacent large cents`, async ({ page, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await mockApi(page, { query: route => send(route, precisionInvoices()) }); await page.goto('/app/invoices/sales');
    await expect(page.getByTestId('summary-count')).toHaveText('7');
    const cell = (label: string) => page.getByRole('row').filter({ has: invoiceCell(page, `PREC-${label}`) }).locator(`[col-id="${column}"]`);
    const normalize = (value: string) => value.replace(/\s/g, '');
    await expect.poll(async () => normalize(await cellValue(cell('HI')).innerText())).toBe('9999999999999999,99');
    await expect.poll(async () => normalize(await cellValue(cell('LO')).innerText())).toBe('9999999999999999,98');
    await expect(cellValue(cell('ZERO'))).toHaveText('0,00'); await expect(cellValue(cell('SMALL'))).toHaveText('123,45');
    await expect(cellValue(cell('MISSING'))).toHaveText(column === 'grossAmount' ? '0,00' : '');
    const header = page.locator(`[role="columnheader"][col-id="${column}"]`);
    const names = page.locator('[role="row"][row-id] [col-id="invoiceNumber"] .copy-cell-text');
    await header.click(); await expect(header).toHaveAttribute('aria-sort', 'ascending');
    await expect(names).toHaveText((column === 'grossAmount'
      ? ['NEG-HI', 'NEG-LO', 'ZERO', 'MISSING', 'SMALL', 'LO', 'HI']
      : ['NEG-HI', 'NEG-LO', 'ZERO', 'SMALL', 'LO', 'HI', 'MISSING']).map(label => `PREC-${label}`));
    await header.click(); await expect(header).toHaveAttribute('aria-sort', 'descending');
    await expect(names).toHaveText((column === 'grossAmount'
      ? ['HI', 'LO', 'SMALL', 'ZERO', 'MISSING', 'NEG-LO', 'NEG-HI']
      : ['MISSING', 'HI', 'LO', 'SMALL', 'ZERO', 'NEG-LO', 'NEG-HI']).map(label => `PREC-${label}`));
    for (const label of ['HI', 'NEG-HI', 'ZERO']) {
      await cell(label).click();
      const displayed = await cellValue(cell(label)).innerText();
      await page.evaluate(() => navigator.clipboard.writeText('sentinel'));
      await cellValue(cell(label)).evaluate(element => {
        const range = document.createRange(); range.selectNodeContents(element);
        const selection = window.getSelection()!; selection.removeAllRanges(); selection.addRange(range);
      });
      await page.keyboard.press('Control+c');
      // Native browser copying may turn a non-breaking grouping space into a regular space.
      await expect.poll(async () => (await page.evaluate(() => navigator.clipboard.readText())).replace(/[\u00a0\u202f]/g, ' ')).toBe(displayed.replace(/[\u00a0\u202f]/g, ' '));
    }
    const filter = page.locator(`.ag-floating-filter[col-id="${column}"] input`);
    for (const [label, value] of [['LO', '9999999999999999,98'], ['HI', '9999999999999999,99'], ['NEG-HI', '-9999999999999999,99']] as const) {
      await filter.fill(value); await expect(page.getByTestId('visible-count')).toHaveText('1');
      await expect(invoiceCell(page, `PREC-${label}`)).toBeVisible();
      await expect(page.getByTestId('summary-count')).toHaveText('1');
      await expect.poll(async () => normalize(await page.getByTestId('summary-gross').innerText())).toBe(value);
    }
    await filter.fill('0'); await expect(page.getByTestId('summary-count')).toHaveText(column === 'grossAmount' ? '2' : '1');
    await expect(invoiceCell(page, 'PREC-ZERO')).toBeVisible(); await expect(page.getByTestId('summary-gross')).toHaveText('0,00');
    await filter.fill(''); await expect(page.getByTestId('visible-count')).toHaveText('7'); await expect(page.getByTestId('summary-gross')).toHaveText('123,45');
  });
}

test('exact amount filter preserves numeric inequalities, ranges and missing-value semantics', async ({ page }) => {
  await mockApi(page, { query: route => send(route, precisionInvoices()) }); await page.goto('/app/invoices/sales');
  await expect(page.getByTestId('summary-count')).toHaveText('7');
  await page.locator('.ag-floating-filter[col-id="netAmount"]').getByRole('button', { name: 'Otwórz Menu Filtrów', exact: true }).click();
  const filter = page.locator('.ag-filter-body');
  const choose = async (name: string) => {
    await page.locator('.ag-filter-select .ag-picker-field-wrapper').click();
    await page.getByRole('option', { name, exact: true }).click();
  };
  const check = async (labels: string[], total: string) => {
    await expect(page.getByTestId('summary-count')).toHaveText(String(labels.length));
    await expect(page.locator('[role="row"][row-id] [col-id="invoiceNumber"] .copy-cell-text')).toHaveText(labels.map(label => `PREC-${label}`));
    await expect.poll(async () => (await page.getByTestId('summary-gross').innerText()).replace(/\s/g, '')).toBe(total);
  };
  for (const [operator, labels] of [
    ['Więcej niż', ['HI']], ['Więcej lub równe', ['HI', 'LO']],
    ['Mniej niż', ['NEG-HI', 'NEG-LO', 'ZERO', 'SMALL']],
    ['Mniej lub równe', ['LO', 'NEG-HI', 'NEG-LO', 'ZERO', 'SMALL']],
    ['Nie równa się', ['HI', 'NEG-HI', 'NEG-LO', 'ZERO', 'SMALL']],
  ] as const) {
    await choose(operator); await filter.locator('input').first().fill('9999999999999999,98');
    await expect(page.getByTestId('summary-count')).toHaveText(String(labels.length));
    await expect(page.locator('[role="row"][row-id] [col-id="invoiceNumber"] .copy-cell-text')).toHaveText(labels.map(label => `PREC-${label}`));
  }
  await choose('Pomiędzy');
  await filter.locator('input').first().fill('9999999999999999,97');
  await filter.locator('input').nth(1).fill('9999999999999999,99');
  // Preserve the existing numeric filter's exclusive range boundaries.
  await check(['LO'], '9999999999999999,98');
  await choose('Puste'); await check(['MISSING'], '0,00');
  await choose('Niepuste'); await check(['HI', 'LO', 'NEG-HI', 'NEG-LO', 'ZERO', 'SMALL'], '123,45');
});

for (const status of [200, 401, 403, 503]) {
  test(`A logs out and B logs in without exposing A state when cancelled A HTTP ${status} arrives`, async ({ page }) => {
    await page.clock.setFixedTime(new Date('2026-11-14T12:00:00+01:00'));
    let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
    const state = await mockApi(page, { loginRequired: true, query: async (route, index) => {
      if (index === 3) { await gate; return send(route, result('OWNER-A-LATE'), status); }
      if (route.request().headers()['x-tenant-id'] === A) return send(route, result('OWNER-A'));
      const body = result('OWNER-B');
      body.items = [{ ...body.items[0], grossAmount: 777, grossAmountExact: '777.00', netAmount: 700, netAmountExact: '700.00', vatAmount: 77, vatAmountExact: '77.00' }];
      body.summaries = [{ currency: 'PLN', invoiceCount: 1, netTotal: 700, vatTotal: 77, grossTotal: 777, missingNetCount: 0, missingVatCount: 0 }];
      return send(route, body);
    } });
    const loginUser = async (user: string) => {
      await page.getByLabel('Login', { exact: true }).fill(user);
      await page.getByLabel('Hasło', { exact: true }).fill('Synthetic-Password-123!');
      await page.getByRole('button', { name: 'Zaloguj', exact: true }).click();
      await expect(page.locator('.session-user')).toHaveText(user);
    };
    await page.goto('/login'); await loginUser('user-A'); await expect(invoiceCell(page, 'OWNER-A-0')).toBeVisible();
    await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
    await page.getByLabel('Data od', { exact: true }).fill('2020-01-01');
    await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
    await expect.poll(() => state.queries.length).toBe(2); await expect(invoiceCell(page, 'OWNER-A-0')).toBeVisible();
    await page.getByLabel('Szukaj w fakturach').fill('OWNER-A-0');
    await page.locator('.ag-floating-filter[col-id="grossAmount"] input').fill('123');
    await expect(page.getByTestId('summary-gross')).toHaveText('123,00');
    await page.getByRole('row').filter({ has: invoiceCell(page, 'OWNER-A-0') }).getByRole('checkbox').check();
    await expect(page.getByTestId('selected-count')).toHaveText('1');
    await page.getByLabel('Data od', { exact: true }).fill('2027-01-01');
    await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Data od nie może być późniejsza');
    await page.getByRole('button', { name: 'Odśwież listę', exact: true }).click();
    await expect.poll(() => state.queries.length).toBe(3);
    await page.getByRole('button', { name: 'Wyloguj', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('grid')).toHaveCount(0);
    await expect(page.getByTestId('summary-gross')).toHaveCount(0); await expect(page.getByRole('alert')).toHaveCount(0);
    // Observe every DOM mutation after logout, including nodes inserted and removed within a single delivery.
    await page.evaluate(() => {
      const violations: string[] = []; (window as unknown as { oldContextLeaks: string[] }).oldContextLeaks = violations;
      const forbidden = /OWNER-A|user-A|Data od nie może być późniejsza|Sesja wygasła|Brak uprawnień|chwilowo niedostępna/;
      new MutationObserver(records => {
        const text = [document.body.textContent ?? '', ...records.flatMap(record => Array.from(record.addedNodes, node => node.textContent ?? ''))].join('\n');
        if (forbidden.test(text)) violations.push('Old identity, invoice or error text');
        if (Array.from(document.querySelectorAll('[data-testid="summary-gross"]')).some(node => /^(123,00|110,70)$/.test(node.textContent ?? ''))) violations.push('Old totals');
        if (document.querySelector('[data-testid="selected-count"]')?.textContent === '1') violations.push('Old selection');
        const from = document.querySelector<HTMLInputElement>('input[type="date"]');
        if (from && ['2020-01-01', '2027-01-01'].includes(from.value)) violations.push('Old date filter');
        if (document.querySelector<HTMLInputElement>('input[type="search"]')?.value.includes('OWNER-A')) violations.push('Old search');
        if (document.querySelector<HTMLInputElement>('.ag-floating-filter[col-id="grossAmount"] input')?.value === '123') violations.push('Old numeric filter');
        if (document.querySelector<HTMLSelectElement>('form.invoice-filters select')?.value === 'InvoicingDate') violations.push('Old date kind');
      }).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true });
    });
    await loginUser('user-B'); await expect(invoiceCell(page, 'OWNER-B-0')).toBeVisible();
    await expect(page.getByLabel('Aktywny tenant')).toHaveValue(B);
    await expect(page.getByTestId('summary-gross')).toHaveText('777,00'); await expect(page.getByTestId('summary-count')).toHaveText('1');
    await expect(page.getByTestId('selected-count')).toHaveText('0'); await expect(page.getByLabel('Szukaj w fakturach')).toHaveValue('');
    await expect(page.locator('.ag-floating-filter[col-id="grossAmount"] input')).toHaveValue('');
    await expect(page.getByLabel('Rodzaj daty', { exact: true })).toHaveValue('IssueDate');
    await expect(page.getByLabel('Data od', { exact: true })).toHaveValue('2026-10-16');
    const late = page.waitForResponse(response => response.url() === state.queries[2].url.href && response.status() === status);
    release(); await (await late).finished();
    // Let the response microtasks and DOM observer finish before inspecting the complete observation history.
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
    await expect(page.locator('.session-user')).toHaveText('user-B'); await expect(invoiceCell(page, 'OWNER-B-0')).toBeVisible();
    await expect(page.getByTestId('summary-gross')).toHaveText('777,00'); await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByLabel('Aktywny tenant')).toHaveValue(B); expect(state.queries).toHaveLength(4);
    expect(await page.evaluate(() => (window as unknown as { oldContextLeaks: string[] }).oldContextLeaks)).toEqual([]);
  });
}

test('summaries use exact currency amounts after grid filters and ignore selection and VAT in PLN', async ({ page }) => {
  const base = result('MONEY').items[0];
  const body = { items: [
    { ...base, ksefNumber: 'M1', invoiceNumber: 'MONEY-1', grossAmount: 0.12, netAmount: 0.1, vatAmount: 0.02, grossAmountExact: '0.12', netAmountExact: '0.10', vatAmountExact: '0.02' },
    { ...base, ksefNumber: 'M2', invoiceNumber: 'MONEY-2', grossAmount: 0.24, netAmount: 0.2, vatAmount: 0.04, grossAmountExact: '0.24', netAmountExact: '0.20', vatAmountExact: '0.04' },
    { ...base, ksefNumber: 'M3', invoiceNumber: 'MONEY-3', currency: 'EUR', grossAmount: -1, netAmount: null, vatAmount: null, grossAmountExact: '-1.00', netAmountExact: null, vatAmountExact: null, vatAmountPln: 999, vatAmountPlnExact: '999.00' },
    { ...base, ksefNumber: 'M4', invoiceNumber: 'MONEY-4', grossAmount: 0, netAmount: 0, vatAmount: 0, grossAmountExact: '0.00', netAmountExact: '0.00', vatAmountExact: '0.00' },
  ], summaries: [
    { currency: 'PLN', invoiceCount: 3, netTotal: 0.3, vatTotal: 0.06, grossTotal: 0.36, missingNetCount: 0, missingVatCount: 0 },
    { currency: 'EUR', invoiceCount: 1, netTotal: null, vatTotal: null, grossTotal: -1, missingNetCount: 1, missingVatCount: 1 },
  ] };
  await mockApi(page, { query: route => send(route, body) }); await page.goto('/app/invoices/sales');
  const summary = page.getByRole('region', { name: 'Podsumowanie przefiltrowanych faktur' });
  await expect(summary.getByTestId('summary-count')).toHaveText('4');
  const pln = summary.locator('[data-currency="PLN"]'); const eur = summary.locator('[data-currency="EUR"]');
  await expect(pln.getByTestId('summary-net')).toHaveText('0,30'); await expect(pln.getByTestId('summary-vat')).toHaveText('0,06');
  await expect(pln.getByTestId('summary-gross')).toHaveText('0,36'); await expect(eur.getByTestId('summary-gross')).toHaveText('-1,00');
  await expect(eur.getByTestId('summary-net')).toHaveText('Brak pełnych danych'); await expect(eur).toContainText('Netto: 1 · VAT: 1');
  await page.getByLabel('Szukaj w fakturach').fill('MONEY-1'); await expect(summary.getByTestId('summary-count')).toHaveText('1');
  await expect(pln.getByTestId('summary-gross')).toHaveText('0,12'); await expect(eur).toHaveCount(0);
  await page.getByRole('row').filter({ has: invoiceCell(page, 'MONEY-1') }).getByRole('checkbox').check();
  await expect(page.getByTestId('selected-count')).toHaveText('1'); await expect(pln.getByTestId('summary-gross')).toHaveText('0,12');
  await page.getByRole('button', { name: 'Resetuj widok' }).click(); await expect(summary.getByTestId('summary-count')).toHaveText('4');
  await page.locator('.ag-floating-filter[col-id="grossAmount"] input').fill('0,24'); await expect(summary.getByTestId('summary-count')).toHaveText('1');
  await expect(pln.getByTestId('summary-net')).toHaveText('0,20'); await expect(pln.getByTestId('summary-vat')).toHaveText('0,04');
  await page.getByLabel('Szukaj w fakturach').fill('NO-MATCH'); await expect(summary.getByTestId('summary-count')).toHaveText('0');
  await expect(summary.getByTestId('summary-gross')).toHaveCount(0);
});

test('summaries include non-rendered rows and retain cents above the JavaScript safe integer range', async ({ page }) => {
  const body = result('BIG');
  body.items = Array.from({ length: 300 }, (_, i) => ({ ...body.items[0], ksefNumber: `BIG-${i}`, invoiceNumber: `BIG-${i}`,
    grossAmount: Number('9999999999999999.99'), grossAmountExact: '9999999999999999.99', netAmount: 0, netAmountExact: '0.00' }));
  await mockApi(page, { query: route => send(route, body) }); await page.goto('/app/invoices/purchase');
  await expect(page.getByTestId('summary-count')).toHaveText('300');
  expect(await page.locator('[role="row"][row-id]').count()).toBeLessThan(100);
  await expect.poll(async () => (await page.getByTestId('summary-gross').innerText()).replace(/\s/g, '')).toBe('2999999999999999997,00');
});

test('empty database results have zero invoice count without invented zero amounts', async ({ page }) => {
  await mockApi(page, { query: route => send(route, { items: [], summaries: [] }) }); await page.goto('/app/invoices/sales');
  await expect(page.getByTestId('summary-count')).toHaveText('0'); await expect(page.getByTestId('summary-gross')).toHaveCount(0);
  await expect(page.getByText('Brak faktur do podsumowania.', { exact: true })).toBeVisible();
});

test('tenant changes and logout remove old summaries, including a late unauthorized reply', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  const state = await mockApi(page, { query: async (route, index) => {
    if (index === 2) { await gate; await send(route, result('OLD'), 401); }
    else { const body = result(index === 1 ? 'A' : 'B'); body.items = body.items.slice(0, index === 1 ? 3 : 1); await send(route, body); }
  } });
  await page.goto('/app/invoices/sales'); await expect(page.getByTestId('summary-count')).toHaveText('3');
  await page.getByRole('button', { name: 'Odśwież listę' }).click(); await expect.poll(() => state.queries.length).toBe(2);
  await expect(page.getByTestId('summary-count')).toHaveCount(0); await page.getByLabel('Aktywny tenant').selectOption(B);
  await expect(page.getByTestId('summary-count')).toHaveText('1'); await expect(page.getByTestId('summary-gross')).toHaveText('123,00');
  const response = page.waitForResponse(res => res.url() === state.queries[1].url.href && res.status() === 401);
  release(); await (await response).finished(); await expect(page.getByTestId('summary-count')).toHaveText('1');
  await page.getByRole('button', { name: 'Wyloguj', exact: true }).click(); await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByTestId('summary-count')).toHaveCount(0);
});

test('date filters, date kinds and invoice directions replace summaries instead of reusing previous totals', async ({ page }) => {
  const state = await mockApi(page, { query: async (route, index) => { const body = result(`QUERY-${index}`); body.items = body.items.slice(0, index === 1 ? 3 : index === 2 ? 2 : 1); await send(route, body); } });
  await page.goto('/app/invoices/sales'); await expect(page.getByTestId('summary-count')).toHaveText('3');
  await page.getByLabel('Data od', { exact: true }).fill('2026-01-01'); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
  await expect(page.getByTestId('summary-count')).toHaveText('2');
  await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate'); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
  await expect(page.getByTestId('summary-count')).toHaveText('1');
  const purchase = page.waitForRequest(request => new URL(request.url()).pathname === '/api/invoices/query' && new URL(request.url()).searchParams.get('direction') === 'Purchase');
  await openPurchases(page); await purchase;
  await expect(page.getByRole('heading', { name: 'Faktury zakupu', exact: true })).toBeVisible();
  await expect(invoiceCell(page, 'QUERY-4-0')).toBeVisible(); await expect(invoiceCell(page, 'QUERY-3-0')).toHaveCount(0);
  await expect(page.getByTestId('summary-count')).toHaveText('1');
  expect(state.queries.at(-1)!.url.searchParams.get('direction')).toBe('Purchase');
});

// Exercise stale-response guards even when a transport does not honor AbortSignal.
test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => String(input).startsWith('/api/invoices/query')
      ? original(input, { ...init, signal: undefined }) : original(input, init);
  });
});

test('redirects to sales, queries 30 days and navigates to purchases without import calls', async ({ page }) => {
  await page.clock.install({ time: new Date('2026-11-14T12:00:00+01:00') });
  const state = await mockApi(page); await page.goto('/app/invoices');
  await expect(page).toHaveURL(/\/app\/invoices\/sales$/);
  await expect(invoiceCell(page, 'A-Sales-0')).toBeVisible();
  expect(Object.fromEntries(state.queries[0].url.searchParams)).toEqual({ from: '2026-10-16', to: '2026-11-14', direction: 'Sales', dateKind: 'IssueDate' });
  await openPurchases(page);
  await expect(page).toHaveURL(/\/app\/invoices\/purchase$/); await expect(invoiceCell(page, 'A-Purchase-0')).toBeVisible();
  await expect(invoiceCell(page, 'A-Sales-0')).toHaveCount(0);
  await expect(page.getByRole('columnheader', { name: 'NIP sprzedawcy' })).toBeVisible();
  expect(state.queries[1].url.searchParams.get('direction')).toBe('Purchase');
  await page.getByRole('button', { name: 'Odśwież listę' }).click(); await expect(invoiceCell(page, 'A-Purchase-0')).toBeVisible();
  expect(state.queries).toHaveLength(3); expect(state.paths.some(path => path.includes('import'))).toBe(false);
});

test('preserves grid search, sorting, numeric filters, selection and reset with API rows', async ({ page }) => {
  await mockApi(page); await page.goto('/app/invoices/sales'); await expect(page.getByTestId('visible-count')).toHaveText('3');
  await page.getByLabel('Szukaj w fakturach').fill('A-Sales-1'); await expect(page.getByTestId('visible-count')).toHaveText('1');
  const row = page.getByRole('row').filter({ has: invoiceCell(page, 'A-Sales-1') }); await row.getByRole('checkbox').check();
  await expect(page.getByTestId('selected-count')).toHaveText('1');
  await page.getByRole('button', { name: 'Resetuj widok' }).click(); await expect(page.getByTestId('selected-count')).toHaveText('0');
  await expect(page.getByTestId('visible-count')).toHaveText('3');
  await page.getByRole('columnheader', { name: 'Numer faktury', exact: true }).click();
  await expect(page.locator('[role="row"][row-index="0"] [col-id="invoiceNumber"] .copy-cell-text')).toHaveText('A-Sales-0');
  await page.getByRole('columnheader', { name: 'Numer faktury', exact: true }).click();
  await expect(page.locator('[role="row"][row-index="0"] [col-id="invoiceNumber"] .copy-cell-text')).toHaveText('A-Sales-2');
  const filter = page.locator('.ag-floating-filter[col-id="grossAmount"] input'); await filter.fill('123');
  await expect(page.getByTestId('visible-count')).toHaveText('1'); await expect(invoiceCell(page, 'A-Sales-0')).toBeVisible();
});

test('clears tenant A rows and selection while tenant B is loading, even with identical KSeF numbers', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  await mockApi(page, { query: async (route) => {
    const tenant = route.request().headers()['x-tenant-id']; if (tenant === B) await gate;
    await send(route, result(tenant === A ? 'A' : 'B'));
  } });
  await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'A-0')).toBeVisible();
  await page.getByRole('row').filter({ has: invoiceCell(page, 'A-0') }).getByRole('checkbox').check();
  await page.getByLabel('Aktywny tenant').selectOption(B);
  await expect(page.getByText('Wczytywanie faktur…', { exact: true })).toBeVisible(); await expect(page.getByRole('grid')).toHaveCount(0);
  await expect(invoiceCell(page, 'A-0')).toHaveCount(0); release(); await expect(invoiceCell(page, 'B-0')).toBeVisible();
  await expect(page.getByTestId('selected-count')).toHaveText('0');
});

test('ignores an old response after A → B → A', async ({ page }) => {
  let release!: () => void; let released!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }); const finished = new Promise<void>(resolve => { released = resolve; });
  const state = await mockApi(page, { query: async (route, index) => {
    if (index === 1) { await gate; await send(route, result('OLD')); released(); }
    else await send(route, result(index === 2 ? 'B' : 'NEW-A'));
  } });
  await page.goto('/app/invoices/sales'); await expect.poll(() => state.queries.length).toBe(1);
  await page.getByLabel('Aktywny tenant').selectOption(B); await expect(invoiceCell(page, 'B-0')).toBeVisible();
  await page.getByLabel('Aktywny tenant').selectOption(A); await expect(invoiceCell(page, 'NEW-A-0')).toBeVisible();
  release(); await finished; await expect(invoiceCell(page, 'OLD-0')).toHaveCount(0); await expect(invoiceCell(page, 'NEW-A-0')).toBeVisible();
});

test('shows a safe error and retries only after an explicit refresh', async ({ page }) => {
  const state = await mockApi(page, { query: async (route, index) => index === 1 ? send(route, { detail: 'private SQL data' }, 503) : send(route, result('OK')) });
  await page.goto('/app/invoices/purchase'); await expect(page.getByRole('alert')).toContainText('niedostępna');
  await expect(page.getByText('private SQL data')).toHaveCount(0); expect(state.queries).toHaveLength(1);
  await page.getByRole('button', { name: 'Spróbuj ponownie' }).click(); await expect(invoiceCell(page, 'OK-0')).toBeVisible(); expect(state.queries).toHaveLength(2);
});

test('shows an empty database result without substituting demo invoices', async ({ page }) => {
  await mockApi(page, { query: route => send(route, { items: [], summaries: [] }) }); await page.goto('/app/invoices/sales');
  await expect(page.getByText('Brak faktur w wybranym okresie.', { exact: true })).toBeVisible(); await expect(page.getByRole('grid')).toHaveCount(0);
  await expect(page.getByText('Brak danych spełniających warunki', { exact: true })).toHaveCount(0);
});

test('does not query invoices without an active tenant', async ({ page }) => {
  const state = await mockApi(page, { emptyTenants: true }); await page.goto('/app/invoices/sales');
  await expect(page.getByText('Brak dostępnych tenantów.', { exact: false }).last()).toBeVisible(); expect(state.queries).toHaveLength(0);
});

test('distinguishes initial loading from an empty result and prevents duplicate refresh', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  const state = await mockApi(page, { query: async route => { await gate; await send(route, result('READY')); } });
  await page.goto('/app/invoices/sales'); await expect(page.getByText('Wczytywanie faktur…', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Odśwież listę' })).toBeDisabled(); await expect(page.getByRole('grid')).toHaveCount(0);
  await expect(page.getByText('Brak faktur w wybranym okresie.', { exact: true })).toHaveCount(0); await expect(page.getByRole('alert')).toHaveCount(0);
  release(); await expect(invoiceCell(page, 'READY-0')).toBeVisible(); expect(state.queries).toHaveLength(1);
  await expect(page.getByText('Wczytywanie faktur…', { exact: true })).toHaveCount(0);
});

test('distinguishes column and search filters hiding all invoices from an empty API result', async ({ page }) => {
  await mockApi(page); await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'A-Sales-0')).toBeVisible();
  const filtered = page.getByText('Brak faktur spełniających ustawione filtry. Zmień filtry lub zresetuj widok.', { exact: true });
  await page.getByLabel('Szukaj w fakturach').fill('nieistniejący-numer'); await expect(filtered).toBeVisible();
  await expect(page.getByText('Brak faktur w wybranym okresie.', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Resetuj widok' }).click(); await expect(filtered).toHaveCount(0);
  await page.locator('.ag-floating-filter[col-id="grossAmount"] input').fill('999'); await expect(filtered).toBeVisible();
  await page.getByRole('button', { name: 'Resetuj widok' }).click(); await expect(page.getByTestId('visible-count')).toHaveText('3');
});

for (const contextChange of ['tenant', 'direction'] as const) for (const status of [200, 401, 403, 503]) {
  test(`ignores late HTTP ${status} after ${contextChange} change without session or error side effects`, async ({ page }) => {
    let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
    const state = await mockApi(page, { query: async (route, index) => {
      if (index === 1) { await gate; await send(route, result('OLD'), status); }
      else await send(route, result('CURRENT'));
    } });
    await page.goto('/app/invoices/sales'); await expect.poll(() => state.queries.length).toBe(1);
    if (contextChange === 'tenant') await page.getByLabel('Aktywny tenant').selectOption(B);
    else await openPurchases(page);
    await expect(invoiceCell(page, 'CURRENT-0')).toBeVisible();
    const response = page.waitForResponse(res => res.url() === state.queries[0].url.href && res.status() === status);
    release(); await (await response).finished();
    await expect(invoiceCell(page, 'CURRENT-0')).toBeVisible(); await expect(invoiceCell(page, 'OLD-0')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Wyloguj', exact: true })).toBeVisible();
    await expect(page.getByLabel('Aktywny tenant')).toHaveValue(contextChange === 'tenant' ? B : A);
    await expect(page.getByRole('alert')).toHaveCount(0);
  });
}

test('logout removes selected rows and ignores a delayed unauthorized invoice response', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  const state = await mockApi(page, { query: async (route, index) => {
    if (index === 1) await send(route, result('SELECTED'));
    else { await gate; await send(route, result('OLD'), 401); }
  } });
  await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'SELECTED-0')).toBeVisible();
  await page.getByRole('row').filter({ has: invoiceCell(page, 'SELECTED-0') }).getByRole('checkbox').check();
  await expect(page.getByTestId('selected-count')).toHaveText('1');
  await page.getByRole('button', { name: 'Odśwież listę' }).click(); await expect.poll(() => state.queries.length).toBe(2);
  await page.getByRole('button', { name: 'Wyloguj', exact: true }).click(); await expect(page).toHaveURL(/\/login$/);
  const response = page.waitForResponse(res => res.url() === state.queries[1].url.href && res.status() === 401);
  release(); await (await response).finished();
  await expect(page.getByRole('grid')).toHaveCount(0); await expect(page.getByTestId('selected-count')).toHaveCount(0);
  await expect(page.getByRole('alert')).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible();
});

test('logout immediately removes the currently selected invoice and table state', async ({ page }) => {
  await mockApi(page); await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'A-Sales-0')).toBeVisible();
  await page.getByRole('row').filter({ has: invoiceCell(page, 'A-Sales-0') }).getByRole('checkbox').check();
  await expect(page.getByTestId('selected-count')).toHaveText('1');
  await page.getByRole('button', { name: 'Wyloguj', exact: true }).click(); await expect(page).toHaveURL(/\/login$/);
  await expect(invoiceCell(page, 'A-Sales-0')).toHaveCount(0); await expect(page.getByRole('grid')).toHaveCount(0);
  await expect(page.getByTestId('selected-count')).toHaveCount(0);
});

test('reports network errors safely and recovers after manual retry', async ({ page }) => {
  const state = await mockApi(page, { query: (route, index) => index === 1 ? route.abort('failed') : send(route, result('RECOVERED')) });
  await page.goto('/app/invoices/sales'); await expect(page.getByRole('alert')).toContainText('Nie udało się połączyć');
  expect(state.queries).toHaveLength(1); await page.getByRole('button', { name: 'Spróbuj ponownie' }).click();
  await expect(invoiceCell(page, 'RECOVERED-0')).toBeVisible(); await expect(page.getByRole('alert')).toHaveCount(0);
});

for (const status of [401, 403]) {
  test(`current invoice HTTP ${status} clears rows and selection through the existing session mechanism`, async ({ page }) => {
    await mockApi(page, { query: (route, index) => index === 1 ? send(route, result('VISIBLE')) : send(route, {}, status) });
    await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'VISIBLE-0')).toBeVisible();
    await page.getByRole('row').filter({ has: invoiceCell(page, 'VISIBLE-0') }).getByRole('checkbox').check();
    await expect(page.getByTestId('selected-count')).toHaveText('1');
    await page.getByRole('button', { name: 'Odśwież listę' }).click();
    if (status === 401) await expect(page).toHaveURL(/\/login$/);
    else {
      await expect(page.getByLabel('Aktywny tenant')).toHaveValue('');
      await expect(page.getByText('Wybierz tenanta, aby otworzyć moduł.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Wyloguj', exact: true })).toBeVisible();
    }
    await expect(page.getByRole('grid')).toHaveCount(0); await expect(page.getByTestId('selected-count')).toHaveCount(0);
  });
}

for (const [path, direction] of [['sales', 'Sales'], ['purchase', 'Purchase']] as const) {
  test(`applies date filters explicitly for ${direction}, sends Warsaw only for KSeF dates and restores defaults`, async ({ page }) => {
    await page.clock.install({ time: new Date('2026-11-14T12:00:00+01:00') });
    const state = await mockApi(page); await page.goto(`/app/invoices/${path}`);
    await expect(page.getByTestId('visible-count')).toHaveText('3');
    await expect(page.getByLabel('Rodzaj daty', { exact: true })).toHaveValue('IssueDate');
    await expect(page.getByLabel('Data od', { exact: true })).toHaveValue('2026-10-16');
    await expect(page.getByLabel('Data do', { exact: true })).toHaveValue('2026-11-14');
    await page.getByLabel('Data od', { exact: true }).fill('2026-03-28'); await page.getByLabel('Data do', { exact: true }).fill('2026-03-30');
    expect(state.queries).toHaveLength(1);
    await page.getByRole('button', { name: 'Filtruj', exact: true }).click(); await expect.poll(() => state.queries.length).toBe(2);
    await expect(page.getByTestId('visible-count')).toHaveText('3');
    expect(Object.fromEntries(state.queries[1].url.searchParams)).toEqual({ from: '2026-03-28', to: '2026-03-30', direction, dateKind: 'IssueDate' });
    await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
    await expect(page.getByText('Strefa czasowa: Europe/Warsaw', { exact: true })).toBeVisible();
    await page.getByLabel('Data od', { exact: true }).fill('2024-02-29'); await page.getByLabel('Data do', { exact: true }).fill('2024-02-29');
    expect(state.queries).toHaveLength(2); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
    await expect.poll(() => state.queries.length).toBe(3); await expect(page.getByTestId('visible-count')).toHaveText('3');
    expect(Object.fromEntries(state.queries[2].url.searchParams)).toEqual({ from: '2024-02-29', to: '2024-02-29', direction, dateKind: 'InvoicingDate', timeZoneId: 'Europe/Warsaw' });
    await page.getByRole('button', { name: 'Przywróć domyślne', exact: true }).click();
    await expect.poll(() => state.queries.length).toBe(4); await expect(page.getByTestId('visible-count')).toHaveText('3');
    await expect(page.getByLabel('Rodzaj daty', { exact: true })).toHaveValue('IssueDate');
    await expect(page.getByLabel('Data od', { exact: true })).toHaveValue('2026-10-16'); await expect(page.getByLabel('Data do', { exact: true })).toHaveValue('2026-11-14');
    expect(Object.fromEntries(state.queries[3].url.searchParams)).toEqual({ from: '2026-10-16', to: '2026-11-14', direction, dateKind: 'IssueDate' });
    await page.getByLabel('Szukaj w fakturach').fill(`A-${direction}-1`); await expect(page.getByTestId('visible-count')).toHaveText('1');
    expect(state.paths.some(value => value.includes('import'))).toBe(false);
  });
}

test('invalid ranges never reach the API and reset recovers even from an empty draft', async ({ page }) => {
  const state = await mockApi(page); await page.goto('/app/invoices/sales'); await expect(page.getByTestId('visible-count')).toHaveText('3');
  await page.getByLabel('Data od', { exact: true }).fill('2026-11-15'); await page.getByLabel('Data do', { exact: true }).fill('2026-11-14');
  await page.getByRole('button', { name: 'Filtruj', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Data od nie może być późniejsza');
  expect(state.queries).toHaveLength(1); await expect(page.getByTestId('visible-count')).toHaveText('3');
  await page.getByLabel('Data od', { exact: true }).fill(''); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Podaj poprawne daty'); expect(state.queries).toHaveLength(1);
  await page.getByRole('button', { name: 'Przywróć domyślne', exact: true }).click();
  await expect.poll(() => state.queries.length).toBe(2); await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.getByTestId('visible-count')).toHaveText('3');
});

for (const status of [200, 401, 403, 503]) {
  test(`rapid date and date-kind changes ignore delayed HTTP ${status} without changing the current session`, async ({ page }) => {
    let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
    const state = await mockApi(page, { query: async (route, index) => {
      if (index === 2) { await gate; await send(route, result('OLD-FILTER'), status); }
      else await send(route, result(index === 1 ? 'INITIAL' : 'NEW-FILTER'));
    } });
    await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'INITIAL-0')).toBeVisible();
    await page.getByLabel('Data od', { exact: true }).fill('2026-01-01'); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
    await expect.poll(() => state.queries.length).toBe(2); await expect(page.getByText('Wczytywanie faktur…', { exact: true })).toBeVisible();
    await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
    await page.getByLabel('Data od', { exact: true }).fill('2026-02-01'); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
    await expect(invoiceCell(page, 'NEW-FILTER-0')).toBeVisible(); expect(state.queries[2].url.searchParams.get('timeZoneId')).toBe('Europe/Warsaw');
    const response = page.waitForResponse(res => res.url() === state.queries[1].url.href && res.status() === status);
    release(); await (await response).finished();
    await expect(invoiceCell(page, 'OLD-FILTER-0')).toHaveCount(0); await expect(invoiceCell(page, 'NEW-FILTER-0')).toBeVisible();
    await expect(page.getByRole('alert')).toHaveCount(0); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(A);
    await expect(page.getByRole('button', { name: 'Wyloguj', exact: true })).toBeVisible();
  });
}

test('tenant change clears an in-flight filtered result and resets draft filters without restoring old data', async ({ page }) => {
  let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
  await page.clock.install({ time: new Date('2026-11-14T12:00:00+01:00') });
  const state = await mockApi(page, { query: async (route, index) => {
    if (index === 2) { await gate; await send(route, result('OLD-TENANT'), 401); }
    else await send(route, result(index === 1 ? 'INITIAL' : 'NEW-TENANT'));
  } });
  await page.goto('/app/invoices/sales'); await expect(invoiceCell(page, 'INITIAL-0')).toBeVisible();
  await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
  await page.getByLabel('Data od', { exact: true }).fill('2026-01-01'); await page.getByRole('button', { name: 'Filtruj', exact: true }).click();
  await expect.poll(() => state.queries.length).toBe(2); await page.getByLabel('Aktywny tenant').selectOption(B);
  await expect(invoiceCell(page, 'INITIAL-0')).toHaveCount(0); await expect(invoiceCell(page, 'NEW-TENANT-0')).toBeVisible();
  await expect(page.getByLabel('Rodzaj daty', { exact: true })).toHaveValue('IssueDate');
  await expect(page.getByLabel('Data od', { exact: true })).toHaveValue('2026-10-16');
  expect(state.queries[2].tenant).toBe(B); expect(state.queries[2].url.searchParams.has('timeZoneId')).toBe(false);
  const response = page.waitForResponse(res => res.url() === state.queries[1].url.href && res.status() === 401);
  release(); await (await response).finished(); await expect(invoiceCell(page, 'NEW-TENANT-0')).toBeVisible();
  await expect(page.getByLabel('Aktywny tenant')).toHaveValue(B); await expect(page.getByRole('alert')).toHaveCount(0);
});
