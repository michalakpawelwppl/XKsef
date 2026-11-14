import { expect, test } from '@playwright/test';
const login = process.env.XKSEF_WEB_TEST_LOGIN;
if (!login || !/^m142-[a-f0-9]{32}$/.test(login)) throw new Error('Set a unique XKSEF_WEB_TEST_LOGIN=m142-<GUID N>. Clean up only these records after this explicit live test.');
const password = 'M142-Synthetic-Password-123!';

test('real HTTPS API + LocalDB: registration, cookie login, duplicate-name tenants, switching, CSRF and logout', async ({ page }) => {
  await page.goto('/register');
  await page.getByLabel('Login', { exact: true }).fill(login);
  await page.getByLabel('E-mail', { exact: true }).fill(`${login}@example.invalid`);
  await page.getByLabel('Hasło', { exact: true }).fill(password);
  await page.getByLabel('Powtórz hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zarejestruj', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('status')).toContainText('Konto zostało utworzone');
  await page.getByLabel('Login', { exact: true }).fill(login);
  await page.getByLabel('Hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zaloguj', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Wyloguj' })).toBeVisible();
  await expect(page.getByLabel('Aktywny tenant')).toHaveValue('');
  const cookie = (await page.context().cookies()).find(value => value.name === '__Host-xksef-session');
  expect(cookie ? { httpOnly: cookie.httpOnly, secure: cookie.secure, sameSite: cookie.sameSite, path: cookie.path } : null)
    .toEqual({ httpOnly: true, secure: true, sameSite: 'Lax', path: '/' });
  // Use the browser's validated HTTPS transport, not Node's separate CA store.
  const request = (path: string, method = 'GET', body?: unknown, headers: Record<string, string> = {}) => page.evaluate(async args => {
    const response = await fetch(args.path, { method: args.method, credentials: 'same-origin', headers: { ...args.headers, ...(args.body === undefined ? {} : { 'Content-Type': 'application/json' }) }, body: args.body === undefined ? undefined : JSON.stringify(args.body) });
    return { status: response.status, data: response.headers.get('Content-Type')?.includes('json') ? await response.json() : null };
  }, { path, method, body, headers });
  const me = await request('/api/browser-auth/me'); expect(me.status).toBe(200); expect(me.data.login).toBe(login);
  const csrf = (await request('/api/browser-auth/csrf')).data as { requestToken: string };
  const tenantIds: string[] = [];
  for (let index = 0; index < 2; index++) {
    const result = await request('/api/tenants', 'POST', { name: `M14.2 ${login}`, nip: '1111111111', taxEntityName: `M14.2 ${login}` }, { 'X-CSRF-TOKEN': csrf.requestToken });
    expect(result.status).toBe(201); tenantIds.push(result.data.tenantId);
  }
  expect(tenantIds[0]).not.toBe(tenantIds[1]);
  await page.getByRole('button', { name: 'Odśwież dostęp' }).click();
  const select = page.getByLabel('Aktywny tenant'); await expect(select.locator('option')).toHaveCount(3);
  await select.selectOption(tenantIds[0]); await expect(page.getByRole('grid', { name: 'Faktury sprzedaży' })).toBeVisible();
  await page.getByLabel('Szukaj w fakturach').fill('FV/2026/000001');
  await select.selectOption(tenantIds[1]); await expect(page.getByLabel('Szukaj w fakturach')).toHaveValue('');
  await select.selectOption(tenantIds[0]); await expect(page.getByLabel('Szukaj w fakturach')).toHaveValue('');
  const denied = await request('/api/tenants', 'POST', { name: 'must not create', nip: '1111111111', taxEntityName: 'must not create' }); expect(denied.status).toBe(400);
  expect((await request('/api/tenants', 'GET', undefined, { Authorization: 'Bearer invalid' })).status).toBe(401);
  expect(await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) }))).toEqual({ local: [], session: [] });
  await page.reload(); await expect(page.getByRole('button', { name: 'Wyloguj' })).toBeVisible();
  await page.getByRole('button', { name: 'Wyloguj' }).click(); await expect(page).toHaveURL(/\/login$/);
  expect((await request('/api/browser-auth/me')).status).toBe(401);
  await page.reload(); await expect(page.getByRole('heading', { name: 'Zaloguj się' })).toBeVisible();
});
