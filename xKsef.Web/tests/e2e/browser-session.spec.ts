import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';

const A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const tenants = [{ id: A, name: 'Wspólna nazwa', role: 'Admin' }, { id: B, name: 'Wspólna nazwa', role: 'User' }];
const password = 'Synthetic-Password-123!';
async function mockApi(page: Page, authenticated = false) {
  const state = { authenticated, csrf: '', tokens: 0, loginStatus: 200, registerStatus: 201, tenantStatus: 200, tenantDelay: 0, logoutStatus: 204, posts: [] as { path: string; csrf: string }[], paths: [] as string[] };
  await page.route('**/api/**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; state.paths.push(path);
    const headers = request.headers();
    expect(headers.authorization).toBeUndefined();
    if (path.startsWith('/api/browser-auth/')) expect(headers['x-tenant-id']).toBeUndefined();
    const send = async (status: number, data: unknown = {}, extra = {}) => {
      try { await route.fulfill({ status, contentType: 'application/json', headers: extra, body: status === 204 ? undefined : JSON.stringify(data) }); }
      catch { /* Requests aborted by context changes are intentionally ignored by the mock transport. */ }
    };
    if (path.endsWith('/csrf')) { state.csrf = `csrf-${++state.tokens}-${state.authenticated}`; return send(200, { requestToken: state.csrf, headerName: 'X-CSRF-TOKEN' }); }
    if (request.method() === 'POST') {
      state.posts.push({ path, csrf: headers['x-csrf-token'] });
      if (headers['x-csrf-token'] !== state.csrf) return send(400, { title: 'CSRF validation failed' });
    }
    if (path.endsWith('/me')) return state.authenticated ? send(200, { userId: 'user-a', login: 'browser-user' }) : send(401);
    if (path.endsWith('/login')) { if (state.loginStatus === 200) state.authenticated = true; return send(state.loginStatus, state.loginStatus === 400 ? { title: 'Invalid CSRF' } : { userId: 'user-a', login: 'browser-user' }, { 'Retry-After': '60' }); }
    if (path.endsWith('/register')) return send(state.registerStatus, state.registerStatus === 201 ? { userId: 'registered' } : { errors: { Identity: ['Ten adres e-mail jest już używany.'] } });
    if (path.endsWith('/logout')) { if (state.logoutStatus === 204) state.authenticated = false; return send(state.logoutStatus); }
    if (path === '/api/tenants') { if (state.tenantDelay) await new Promise(resolve => setTimeout(resolve, state.tenantDelay)); return send(state.tenantStatus, tenants); }
    if (path === '/api/invoices/query') {
      const item = { ksefNumber: 'fixture-1', invoiceNumber: 'FV/2026/000001', sellerName: 'Sprzedawca', sellerNip: '1234567890',
        buyerName: 'Nabywca', buyerNip: '0987654321', issueDate: '2026-11-01', invoicingDate: null,
        currency: 'PLN', grossAmount: 123, netAmount: 100, vatAmount: 23, vatAmountPln: 23,
        grossAmountExact: '123.00', netAmountExact: '100.00', vatAmountExact: '23.00', vatAmountPlnExact: '23.00' };
      return send(200, { items: [item], summaries: [{ currency: 'PLN', invoiceCount: 1, netTotal: 100, vatTotal: 23, grossTotal: 123, missingNetCount: 0, missingVatCount: 0 }] });
    }
    throw new Error(`Unexpected API call: ${path}`);
  });
  return state;
}
async function login(page: Page) {
  await page.getByLabel('Login', { exact: true }).fill('browser-user');
  await page.getByLabel('Hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zaloguj', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Wyloguj', exact: true })).toBeVisible();
}

test('cookie login, /me, lazy invoice module and ERP shell without web storage', async ({ page }) => {
  const state = await mockApi(page); const chunks: string[] = [];
  page.on('request', request => { if (request.url().includes('/assets/')) chunks.push(request.url()); });
  await page.goto('/login'); await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible();
  expect(chunks.some(url => url.includes('InvoicePrototypePage'))).toBe(false);
  await login(page); await expect(page.getByRole('grid', { name: 'Faktury sprzedaży' })).toBeVisible();
  await expect(page.getByLabel('Aktywny tenant')).toHaveValue(A); await expect(page.getByText('Rola: Admin', { exact: true })).toBeVisible();
  expect(state.posts).toEqual([{ path: '/api/browser-auth/login', csrf: 'csrf-1-false' }]); expect(state.tokens).toBe(2);
  await page.getByRole('button', { name: 'Rozwiń menu', exact: true }).click();
  for (const label of ['Banki i rozrachunki', 'Księgowość', 'Podatki', 'Środki trwałe', 'Kadry i płace', 'Administracja']) await expect(page.getByRole('button', { name: `${label} W przygotowaniu`, exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Kartoteki', exact: true }).click();
  const contractors = page.getByRole('link', { name: 'Kontrahenci', exact: true });
  await expect(contractors).toBeVisible();
  await expect(contractors).toBeEnabled();
  await expect(contractors).toHaveAttribute('href', '/app/contractors');
  expect(await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) }))).toEqual({ local: [], session: [] });
  expect(state.paths.filter(path => path.startsWith('/api/invoices'))).toEqual(['/api/invoices/query']);
  await page.screenshot({ path: 'test-results/browser-shell.png', fullPage: true });
});

test('registration validates matching passwords and returns to login without signing in', async ({ page }) => {
  const state = await mockApi(page); await page.goto('/register');
  await page.getByLabel('Login', { exact: true }).fill('new-user'); await page.getByLabel('E-mail', { exact: true }).fill('new@example.invalid');
  await page.getByLabel('Hasło', { exact: true }).fill(password); await page.getByLabel('Powtórz hasło', { exact: true }).fill('other');
  await page.getByRole('button', { name: 'Zarejestruj', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Hasła muszą być identyczne'); expect(state.posts).toHaveLength(0);
  await page.getByLabel('Powtórz hasło', { exact: true }).fill(password); await page.getByRole('button', { name: 'Zarejestruj', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('status')).toContainText('Konto zostało utworzone');
  await expect(page.getByLabel('Hasło', { exact: true })).toHaveValue(''); expect(state.authenticated).toBe(false); expect(state.posts.map(post => post.path)).toEqual(['/api/browser-auth/register']);
});

test('Identity validation is readable and passwords are cleared after a failed registration', async ({ page }) => {
  const state = await mockApi(page); state.registerStatus = 400; await page.goto('/register');
  await page.getByLabel('Login', { exact: true }).fill('new-user'); await page.getByLabel('E-mail', { exact: true }).fill('new@example.invalid');
  await page.getByLabel('Hasło', { exact: true }).fill(password); await page.getByLabel('Powtórz hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zarejestruj', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('Ten adres e-mail jest już używany.');
  await expect(page.getByLabel('Hasło', { exact: true })).toHaveValue(''); await expect(page.getByLabel('Powtórz hasło', { exact: true })).toHaveValue(''); expect(state.posts).toHaveLength(1);
});

test('logout clears tenant data and refreshes anonymous CSRF; reload restores from /me', async ({ page }) => {
  const state = await mockApi(page, true); await page.goto('/app/invoices');
  await expect(page.getByRole('button', { name: 'Wyloguj' })).toBeVisible(); await page.reload(); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(A);
  await page.getByRole('button', { name: 'Wyloguj' }).click(); await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('status')).toContainText('Wylogowano.');
  await expect(page.getByLabel('Aktywny tenant')).toHaveCount(0); await expect(page.getByRole('grid')).toHaveCount(0);
  expect(state.csrf).toMatch(/false$/); expect(state.authenticated).toBe(false);
  await page.reload(); await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible();
});

test('tenant A → B → A resets table filters and selection with duplicate tenant names', async ({ page }) => {
  await mockApi(page, true); await page.goto('/app/invoices');
  const select = page.getByLabel('Aktywny tenant'); await expect(select).toHaveValue(A);
  await page.getByLabel('Szukaj w fakturach').fill('FV/2026/000001');
  const first = page.getByRole('row').filter({ has: page.getByRole('gridcell', { name: 'FV/2026/000001', exact: true }) });
  await first.getByRole('checkbox').check(); await expect(page.getByTestId('selected-count')).toHaveText('1');
  await select.selectOption(B); await expect(page.getByLabel('Szukaj w fakturach')).toHaveValue(''); await expect(page.getByTestId('selected-count')).toHaveText('0');
  await expect(page.getByText('Rola: User', { exact: true })).toBeVisible();
  await select.selectOption(A); await expect(page.getByLabel('Szukaj w fakturach')).toHaveValue(''); await expect(page.getByTestId('selected-count')).toHaveText('0');
});

test('expired cookie clears identity and tenant UI on a session-scoped 401', async ({ page }) => {
  const state = await mockApi(page, true); await page.goto('/app/invoices'); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(A);
  state.tenantStatus = 401; state.authenticated = false; await page.getByRole('button', { name: 'Odśwież dostęp' }).click();
  await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('status')).toContainText('Sesja wygasła'); await expect(page.getByRole('grid')).toHaveCount(0);
});

test('a delayed tenant list cannot restore state after logout', async ({ page }) => {
  const state = await mockApi(page, true); await page.goto('/app/invoices'); await expect(page.getByLabel('Aktywny tenant')).toHaveValue(A);
  state.tenantDelay = 500; const before = state.paths.filter(path => path === '/api/tenants').length;
  await page.getByRole('button', { name: 'Odśwież dostęp' }).click(); await expect.poll(() => state.paths.filter(path => path === '/api/tenants').length).toBeGreaterThan(before);
  await page.getByRole('button', { name: 'Wyloguj' }).click(); await expect(page).toHaveURL(/\/login$/);
  await page.waitForTimeout(650); await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible(); await expect(page.getByLabel('Aktywny tenant')).toHaveCount(0);
});

for (const [status, message] of [[401, 'Nieprawidłowy'], [429, 'Zbyt wiele'], [503, 'niedostępna']] as const) {
  test(`login HTTP ${status} is readable and never automatically retries`, async ({ page }) => {
    const state = await mockApi(page); state.loginStatus = status; await page.goto('/login');
    await page.getByLabel('Login', { exact: true }).fill('browser-user'); await page.getByLabel('Hasło', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Zaloguj', exact: true }).click(); await expect(page.getByRole('alert')).toContainText(message);
    await expect(page.getByLabel('Hasło', { exact: true })).toHaveValue(''); expect(state.posts).toHaveLength(1);
  });
}

test('a CSRF rejection never triggers automatic login retry', async ({ page }) => {
  const state = await mockApi(page); state.loginStatus = 400; await page.goto('/login');
  await page.getByLabel('Login', { exact: true }).fill('browser-user'); await page.getByLabel('Hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zaloguj', exact: true }).click(); await expect(page.getByRole('alert')).toContainText('zabezpieczenia formularza'); expect(state.posts).toHaveLength(1);
});

test('logout failure clears local data but does not claim the cookie was revoked', async ({ page }) => {
  const state = await mockApi(page, true); state.logoutStatus = 503; await page.goto('/app/invoices'); await page.getByRole('button', { name: 'Wyloguj' }).click();
  await expect(page.getByRole('alert')).toContainText('Nie udało się potwierdzić wylogowania'); await expect(page.getByRole('grid')).toHaveCount(0);
  expect(state.posts.filter(post => post.path.endsWith('/logout'))).toHaveLength(1);
});
