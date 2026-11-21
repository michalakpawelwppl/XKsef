import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { test, loginUser, tenantA, tenantB } from '../fixtures/sharedBrowserSession';
import type { SharedSessionApi } from '../fixtures/sharedBrowserSession';

const grants = [
  { id: tenantA, name: 'Pierwsza firma', role: 'User' as const },
  { id: tenantB, name: 'Wybrana firma', role: 'User' as const },
];
async function openForm(page: Page, api: SharedSessionApi) {
  api.setTenants('user-A', grants);
  await page.goto('/login'); await loginUser(page, 'user-A');
  await page.goto('/app/contractors');
  await page.getByLabel('Aktywny tenant').selectOption(tenantB);
  await page.getByRole('button', { name: 'Nowy kontrahent', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Nowy kontrahent', exact: true });
  await dialog.getByLabel('Nazwa pełna *', { exact: true }).fill('Niezapisana firma');
  await dialog.getByLabel('Nazwa skrócona *', { exact: true }).fill('AKR');
  await dialog.getByLabel('NIP', { exact: true }).fill('8567346215');
  await dialog.getByLabel('Miejscowość', { exact: true }).fill('Poznań');
  await dialog.getByLabel('Numer budynku', { exact: true }).fill('12');
  await dialog.evaluate(node => { (window as unknown as { originalContractor: Element }).originalContractor = node; });
  return dialog;
}
async function returnToTab(page: Page) {
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await page.bringToFront();
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
  });
}
async function sameDraft(page: Page) {
  const dialog = page.getByRole('dialog', { name: 'Nowy kontrahent', exact: true });
  await expect(dialog).toBeVisible();
  expect(await dialog.evaluate(node => node === (window as unknown as { originalContractor: Element }).originalContractor)).toBe(true);
  await expect(dialog.getByLabel('Nazwa pełna *', { exact: true })).toHaveValue('Niezapisana firma');
  await expect(dialog.getByLabel('Nazwa skrócona *', { exact: true })).toHaveValue('AKR');
  await expect(dialog.getByLabel('Miejscowość', { exact: true })).toHaveValue('Poznań');
  await expect(page.getByLabel('Aktywny tenant')).toHaveValue(tenantB);
}

test('unchanged session preserves the exact dialog and draft while checking and after metadata changes', async ({ page, sessionApi }) => {
  await openForm(page, sessionApi);
  sessionApi.setTenants('user-A', [{ ...grants[1], name: 'Nowa nazwa' }, grants[0]]);
  const csrfBefore = sessionApi.requests.filter(r => r.path.endsWith('/csrf')).length;
  const listBefore = sessionApi.requests.filter(r => r.path === '/api/contractors').length;
  const verification = sessionApi.holdNextIdentity(page);
  await returnToTab(page); await verification.arrived; await sameDraft(page);
  const tenants = page.waitForResponse(response => new URL(response.url()).pathname === '/api/tenants');
  verification.release(); await tenants;
  await sameDraft(page);
  expect(sessionApi.requests.filter(r => r.path.endsWith('/csrf'))).toHaveLength(csrfBefore);
  expect(sessionApi.requests.filter(r => r.path === '/api/contractors')).toHaveLength(listBefore);
  expect(await page.evaluate(() => (window as unknown as { sessionSignals: unknown[] }).sessionSignals)).toEqual([]);
});

for (const outcome of [503, 'network'] as const) {
  test(`verification failure ${outcome} retains the draft and provides reachable recovery above the form`, async ({ page, sessionApi }) => {
    await openForm(page, sessionApi);
    const verification = sessionApi.holdNextIdentity(page);
    await returnToTab(page); await verification.arrived; verification.release(outcome);
    const notice = page.getByRole('dialog', { name: 'Weryfikacja sesji', exact: true });
    await expect(notice).toBeVisible();
    await expect(page.locator('.contractor-dialog')).toBeAttached();
    await notice.getByRole('button', { name: 'Ponów weryfikację sesji' }).click();
    await expect(notice).toHaveCount(0);
    await sameDraft(page);
    expect(await page.evaluate(() => (window as unknown as { sessionSignals: unknown[] }).sessionSignals)).toEqual([]);
  });
}

for (const mode of ['removed', 'role', 'expired'] as const) {
  test(`confirmed ${mode} invalidates the old contractor context`, async ({ page, sessionApi }) => {
    await openForm(page, sessionApi);
    if (mode === 'expired') sessionApi.revokeAll();
    else sessionApi.setTenants('user-A', mode === 'removed' ? [grants[0]] : [grants[0], { ...grants[1], role: 'Admin' }]);
    await returnToTab(page);
    await expect(page.locator('.contractor-dialog')).toHaveCount(0);
    if (mode === 'expired') await expect(page).toHaveURL(/\/login$/);
    else await expect(page.getByLabel('Aktywny tenant')).toHaveValue(mode === 'removed' ? '' : tenantB);
  });
}

test('a contractor save admitted before visibility verification is neither aborted nor repeated', async ({ page, sessionApi }) => {
  await page.addInitScript(() => {
    const original = window.fetch.bind(window);
    (window as unknown as { contractorAborts: number }).contractorAborts = 0;
    window.fetch = (input, init) => {
      if (String(input) === '/api/contractors' && init?.method === 'POST')
        init.signal?.addEventListener('abort', () => { (window as unknown as { contractorAborts: number }).contractorAborts++; });
      return original(input, init);
    };
  });
  const dialog = await openForm(page, sessionApi);
  const save = sessionApi.holdNextContractorSave();
  await dialog.getByRole('button', { name: 'Zapisz', exact: true }).click(); await save.arrived;
  const verification = sessionApi.holdNextIdentity(page);
  await returnToTab(page); await verification.arrived;
  const tenants = page.waitForResponse(response => new URL(response.url()).pathname === '/api/tenants');
  verification.release(); await tenants;
  expect(await page.evaluate(() => (window as unknown as { contractorAborts: number }).contractorAborts)).toBe(0);
  const saved = page.waitForResponse(response => new URL(response.url()).pathname === '/api/contractors' && response.request().method() === 'POST');
  save.release(); await saved;
  await expect(dialog).toHaveCount(0);
  expect(sessionApi.requests.filter(r => r.path === '/api/contractors' && r.status === 201)).toHaveLength(1);
});
