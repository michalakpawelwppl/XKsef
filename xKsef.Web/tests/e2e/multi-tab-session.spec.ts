import { expect } from '@playwright/test';
import type { Page } from '@playwright/test';
import { test, loginUser, openSharedTabs, tenantA, tenantB } from '../fixtures/sharedBrowserSession';

test.beforeEach(async ({ context }) => {
  await context.addInitScript(() => {
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => String(input).startsWith('/api/invoices/query') ? original(input, { ...init, signal: undefined }) : original(input, init);
  });
});

interface Observation { phase: string; text: string; user: string | null; gross: string | null; source: string }
interface Audit { phase: string; observations: Observation[]; capture(source: string, inserted?: string): void }

async function observe(page: Page) {
  await page.evaluate(() => {
    const audit: Audit = { phase: 'loaded', observations: [], capture(source, inserted = '') {
      this.observations.push({ phase: this.phase, source, text: (document.body.textContent ?? '') + '\n' + inserted,
        user: document.querySelector('.session-user')?.textContent ?? null,
        gross: document.querySelector('[data-testid="summary-gross"]')?.textContent ?? null });
    } };
    (window as unknown as { sessionAudit: Audit }).sessionAudit = audit;
    new MutationObserver(records => audit.capture('mutation', records.flatMap(record => Array.from(record.addedNodes, node => node.textContent ?? '')).join('\n')))
      .observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true });
    window.addEventListener('focus', () => audit.capture('focus'));
    document.addEventListener('visibilitychange', () => audit.capture('visibilitychange'));
    audit.capture('start');
  });
}

async function phase(page: Page, name: string) {
  await page.evaluate(name => { const audit = (window as unknown as { sessionAudit: Audit }).sessionAudit; audit.phase = name; audit.capture('checkpoint'); }, name);
}

async function foreground(page: Page) {
  await page.bringToFront();
  await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
}

async function returnToTab(page: Page) {
  // Chromium headless reports both pages as visible even after bringToFront().
  // Drive the visibility transition explicitly while retaining real pages, cookies and BroadcastChannel.
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

async function observations(page: Page) { return page.evaluate(() => (window as unknown as { sessionAudit: Audit }).sessionAudit.observations); }
const invoice = (page: Page, number: string) => page.getByRole('gridcell', { name: number, exact: true });
async function logout(page: Page) {
  await page.bringToFront(); await page.getByRole('button', { name: 'Wyloguj', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('grid')).toHaveCount(0);
}
async function probe(page: Page, path = '/api/invoices/query?from=2026-11-01&to=2026-11-30&direction=Sales', tenant = tenantA) {
  // Raw fetch separates the mocked server's decision from ApiClient's changes to local React state.
  return page.evaluate(async ({ path, tenant }) => {
    const response = await fetch(path, { credentials: 'same-origin', headers: { 'X-Tenant-Id': tenant } });
    return { status: response.status, body: await response.text() };
  }, { path, tenant });
}

test('remote logout immediately hides the peer context and verifies the server without echoing a signal', async ({ page, sessionApi }) => {
  const other = await openSharedTabs(page, sessionApi); await observe(other);
  await other.getByLabel('Szukaj w fakturach').fill('OWNER-A-0');
  await other.getByRole('row').filter({ has: invoice(other, 'OWNER-A-0') }).getByRole('checkbox').check();
  await expect(other.getByTestId('selected-count')).toHaveText('1');
  const verification = sessionApi.holdNextIdentity(other);
  await logout(page); await verification.arrived;
  await expect(other.getByRole('grid')).toHaveCount(0); await expect(other.getByTestId('summary-gross')).toHaveCount(0);
  await expect(other.getByTestId('selected-count')).toHaveCount(0); await phase(other, 'invalidated');
  verification.release();
  await expect(other).toHaveURL(/\/login$/); await expect(other.getByRole('grid')).toHaveCount(0);
  await expect(other.getByTestId('summary-gross')).toHaveCount(0); await expect(other.getByTestId('selected-count')).toHaveCount(0);
  await foreground(other); await expect(other.getByRole('heading', { name: 'Zaloguj się', exact: true })).toBeVisible();
  const denied = await probe(other); expect(denied.status).toBe(401); expect(denied.body).not.toContain('OWNER-A');
  expect((await observations(other)).filter(item => item.phase === 'invalidated').every(item => !item.text.includes('OWNER-A') && item.user === null && item.gross === null)).toBe(true);
  await assertSignals(page, other, 2);
});

test('A logout and B login restore only confirmed B identity, invoices and clean table state in the peer', async ({ page, sessionApi }) => {
  const other = await openSharedTabs(page, sessionApi); await observe(other);
  await other.getByLabel('Data od', { exact: true }).fill('2020-01-01');
  await other.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
  await other.getByRole('button', { name: 'Filtruj', exact: true }).click(); await expect(invoice(other, 'OWNER-A-0')).toBeVisible();
  await other.getByLabel('Szukaj w fakturach').fill('OWNER-A-0');
  await other.getByRole('row').filter({ has: invoice(other, 'OWNER-A-0') }).getByRole('checkbox').check();
  await expect(other.getByTestId('selected-count')).toHaveText('1');
  await logout(page); await expect(other).toHaveURL(/\/login$/); await phase(other, 'invalidated');
  await loginUser(page, 'user-B'); await expect(invoice(page, 'OWNER-B-0')).toBeVisible();
  await expect(other.locator('.session-user')).toHaveText('user-B'); await expect(invoice(other, 'OWNER-B-0')).toBeVisible();
  await expect(other.getByLabel('Aktywny tenant')).toHaveValue(tenantB);
  await expect(other.getByTestId('selected-count')).toHaveText('0'); await expect(other.getByLabel('Szukaj w fakturach')).toHaveValue('');
  await expect(other.getByLabel('Rodzaj daty', { exact: true })).toHaveValue('IssueDate');
  await expect(other.getByLabel('Data od', { exact: true })).not.toHaveValue('2020-01-01');
  expect(sessionApi.requests.some(request => request.path === '/api/invoices/query' && request.owner === 'user-B' && request.tenant === tenantB && request.status === 200)).toBe(true);
  expect((await observations(other)).filter(item => item.phase === 'invalidated').every(item => item.user !== 'user-A' && !item.text.includes('OWNER-A'))).toBe(true);
  await assertSignals(page, other, 3);
});

test('returning to a tab after server revocation hides old data and verifies session and tenant access', async ({ page, sessionApi }) => {
  const other = await openSharedTabs(page, sessionApi); await observe(page); await observe(other);
  await foreground(page); await expect(invoice(page, 'OWNER-A-0')).toBeVisible();
  sessionApi.revokeAll(); const verification = sessionApi.holdNextIdentity(other);
  await returnToTab(other); await verification.arrived;
  await expect(other.getByRole('grid')).toHaveCount(1);
  verification.release(); await expect(other).toHaveURL(/\/login$/);
  await phase(other, 'invalidated');
  // The identity change detected on visibility also notifies the other tab, without an echo loop.
  await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('grid')).toHaveCount(0);
  for (const tab of [page, other]) {
    const denied = await probe(tab); expect(denied.status).toBe(401); expect(denied.body).not.toContain('OWNER-A');
  }
  await expect(other.getByRole('grid')).toHaveCount(0); await expect(other.getByTestId('summary-gross')).toHaveCount(0);
  expect((await observations(other)).filter(item => item.phase === 'invalidated').every(item => !item.text.includes('OWNER-A'))).toBe(true);
  await assertSignals(page, other, 2);
});

for (const status of [200, 401, 403, 503]) {
  test(`remote logout prevents a late HTTP ${status} from restoring peer data even when fetch ignores abort`, async ({ page, sessionApi }) => {
    const other = await openSharedTabs(page, sessionApi); await observe(other);
    const pending = sessionApi.holdNextQuery(status === 401, status);
    await other.getByRole('button', { name: 'Odśwież listę', exact: true }).click(); await pending.arrived;
    await expect(other.getByRole('grid')).toHaveCount(0); await expect(other.getByTestId('summary-gross')).toHaveCount(0);
    await logout(page); await expect(other).toHaveURL(/\/login$/); await phase(other, 'invalidated');
    const reply = other.waitForResponse(response => new URL(response.url()).pathname === '/api/invoices/query' && response.status() === status);
    pending.release(); await (await reply).finished(); await foreground(other);
    await expect(other).toHaveURL(/\/login$/); await expect(other.getByRole('grid')).toHaveCount(0);
    expect((await observations(other)).some(item => item.phase === 'invalidated' && /LATE-A|OWNER-A/.test(item.text))).toBe(false);
    await assertSignals(page, other, 2);
  });
}

for (const outcome of [503, 'network'] as const) {
  test(`visibility verification error ${outcome} preserves mounted context and permits explicit retry without declaring logout`, async ({ page, sessionApi }) => {
    const other = await openSharedTabs(page, sessionApi); await observe(other);
    await foreground(page); await expect(invoice(page, 'OWNER-A-0')).toBeVisible();
    const verification = sessionApi.holdNextIdentity(other);
    await returnToTab(other); await verification.arrived; await expect(other.getByRole('grid')).toHaveCount(1); await phase(other, 'checking');
    verification.release(outcome);
    await expect(other.getByRole('dialog', { name: 'Weryfikacja sesji' })).toBeVisible();
    await expect(other.locator('[role="grid"]')).toHaveCount(1); await expect(other.getByRole('heading', { name: 'Zaloguj się', exact: true })).toHaveCount(0);
    expect((await probe(other, '/api/browser-auth/me')).status).toBe(200);
    expect((await observations(other)).every(item => !item.text.includes('Sesja wygasła'))).toBe(true);
    await phase(other, 'retry'); await other.getByRole('button', { name: 'Ponów weryfikację sesji', exact: true }).click();
    await expect(other.locator('.session-user')).toHaveText('user-A'); await expect(invoice(other, 'OWNER-A-0')).toBeVisible();
    await assertSignals(page, other, 1);
  });
}

async function assertSignals(page: Page, other: Page, count: number) {
  const messages = (await Promise.all([page, other].map(tab => tab.evaluate(() => (window as unknown as { sessionSignals: unknown[] }).sessionSignals)))).flat();
  expect(messages).toHaveLength(count);
  expect(messages.every(message => message === 'session-changed')).toBe(true);
}
