import { readFileSync } from 'node:fs';
import { test, expect, login, request, password } from './liveFixture';

test.describe.configure({ mode: 'serial' });
const cell = (page: import('@playwright/test').Page, value: string) => page.getByRole('gridcell', { name: value, exact: true });
const compact = (text: string) => text.replace(/\s/g, '');

test('HTTPS cookie restoration, SQL invoices, exact grid operations, dates and real XML/PDF', async ({ page, live }) => {
  await login(page, live.loginA);
  const select = page.getByLabel('Aktywny tenant'); await expect(select.locator('option')).toHaveCount(4);
  await select.selectOption(live.tenants[0]); await expect(cell(page, 'LIVE-HI')).toBeVisible();
  const cookie = (await page.context().cookies()).find(value => value.name === '__Host-xksef-session')!;
  expect({ httpOnly: cookie.httpOnly, secure: cookie.secure, sameSite: cookie.sameSite, path: cookie.path, domain: cookie.domain })
    .toEqual({ httpOnly: true, secure: true, sameSite: 'Lax', path: '/', domain: 'localhost' });
  expect(await page.evaluate(() => document.cookie)).not.toContain('__Host-xksef-session');
  await page.reload(); await expect(page.locator('.session-user')).toHaveText(live.loginA);
  await select.selectOption(live.tenants[0]); await expect(page.getByTestId('summary-count')).toHaveText('6');
  const me = await request(page, '/api/browser-auth/me'); expect(me.status).toBe(200); expect(me.data.login).toBe(live.loginA);
  expect((await request(page, '/api/tenants')).data.map((tenant: { id: string }) => tenant.id).sort()).toEqual(live.tenants.slice(0, 3).sort());
  await expect(page.getByTestId('summary-net')).toHaveText('Brak pełnych danych'); await expect(page.getByTestId('summary-vat')).toHaveText('Brak pełnych danych');
  await expect.poll(async () => compact(await page.getByTestId('summary-gross').innerText())).toBe('10000000000000123,43');
  const header = page.locator('[role="columnheader"][col-id="grossAmount"]');
  await header.click(); await expect(page.locator('[role="row"][row-id] [col-id="invoiceNumber"]')).toHaveText(['LIVE-NEG', 'LIVE-MISSING', 'LIVE-ZERO', 'LIVE-SMALL', 'LIVE-LO', 'LIVE-HI']);
  await header.click(); await expect(page.locator('[role="row"][row-id] [col-id="invoiceNumber"]')).toHaveText(['LIVE-HI', 'LIVE-LO', 'LIVE-SMALL', 'LIVE-MISSING', 'LIVE-ZERO', 'LIVE-NEG']);
  for (const column of ['netAmount', 'vatAmount', 'grossAmount']) {
    const filter = page.locator(`.ag-floating-filter[col-id="${column}"] input`);
    for (const [value, name] of [['9999999999999999,98', 'LIVE-LO'], ['9999999999999999,99', 'LIVE-HI'], ['-9999999999999999,99', 'LIVE-NEG']]) {
      await filter.fill(value); await expect(page.getByTestId('summary-count')).toHaveText('1'); await expect(cell(page, name)).toBeVisible();
      for (const sum of ['net', 'vat', 'gross']) await expect.poll(async () => compact(await page.getByTestId(`summary-${sum}`).innerText())).toBe(value);
    }
    await filter.fill(''); await expect(page.getByTestId('summary-count')).toHaveText('6');
  }
  await page.getByLabel('Data od', { exact: true }).fill(live.today); await page.getByLabel('Data do', { exact: true }).fill(live.today);
  await page.getByRole('button', { name: 'Filtruj', exact: true }).click(); await expect(page.getByTestId('summary-count')).toHaveText('6');
  await page.getByLabel('Rodzaj daty', { exact: true }).selectOption('InvoicingDate');
  await page.getByRole('button', { name: 'Filtruj', exact: true }).click(); await expect(cell(page, 'LIVE-DATE')).toBeVisible();
  await expect(page.getByTestId('summary-count')).toHaveText('1'); await expect(page.getByTestId('summary-gross')).toHaveText('11,11');
  await page.getByRole('link', { name: 'Faktury zakupu', exact: true }).click(); await expect(cell(page, 'LIVE-PURCHASE')).toBeVisible();
  for (const sum of ['net', 'vat', 'gross']) await expect(page.getByTestId(`summary-${sum}`)).toHaveText('246,90');
  await select.selectOption(live.tenants[1]); await page.getByRole('link', { name: 'Faktury sprzedaży', exact: true }).click(); await expect(cell(page, 'LIVE-SECOND')).toBeVisible();
  await select.selectOption(live.tenants[2]); await expect(page.getByRole('status')).toHaveText('Brak faktur w wybranym okresie.');
  await expect(page.getByRole('grid')).toHaveCount(0); await expect(page.getByTestId('summary-count')).toHaveText('0');
  const xml = await request(page, `/api/invoices/${live.shared}/xml`, live.tenants[0]);
  expect(xml.status).toBe(200); expect(xml.type).toContain('xml'); expect(xml.text).toBe(live.documentXml);
  const pdf = await request(page, `/api/invoices/${live.shared}/pdf`, live.tenants[0]);
  expect(pdf.status).toBe(200); expect(pdf.type).toBe('application/pdf'); expect(pdf.text.startsWith('%PDF-')).toBe(true); expect(pdf.size).toBeGreaterThan(500);
  expect(await page.evaluate(() => ({ local: Object.keys(localStorage), session: Object.keys(sessionStorage) }))).toEqual({ local: [], session: [] });
  const log = process.env.XKSEF_LIVE_API_LOG;
  if (log) {
    const text = readFileSync(log, 'utf8');
    // A failed assertion must not print the secret being searched for or the complete log.
    expect(text.includes(password), 'API log contains the synthetic password').toBe(false);
    expect(text.includes(cookie.value), 'API log contains the session cookie').toBe(false);
    expect(/(?:Cookie|Authorization):\s*(?:__Host-|Bearer)/i.test(text), 'API log contains authentication headers').toBe(false);
  }
});

test('real SQL tenant isolation, document authorization, revoked membership and expired sessions', async ({ page, live, browser, baseURL }) => {
  await login(page, live.loginA);
  const contextB = await browser.newContext({ baseURL, ignoreHTTPSErrors: false }); const other = await contextB.newPage();
  try {
    await login(other, live.loginB);
    const query = `/api/invoices/query?from=${live.old}&to=${live.today}&direction=Sales&dateKind=IssueDate`;
    for (const [client, own, foreign] of [[page, live.tenants[0], live.tenants[3]], [other, live.tenants[3], live.tenants[0]]] as const) {
      const allowed = await request(client, query, own); expect(allowed.status).toBe(200);
      expect(allowed.data.items.every((row: { invoiceNumber: string }) => client === page ? !row.invoiceNumber.startsWith('LIVE-B-') : row.invoiceNumber.startsWith('LIVE-B-'))).toBe(true);
      const denied = await request(client, query, foreign); expect(denied.status).toBe(403); expect(denied.text).not.toContain('LIVE-');
      for (const format of ['xml', 'pdf']) {
        const result = await request(client, `/api/invoices/${live.shared}/${format}`, foreign); expect(result.status).toBe(403); expect(result.text).not.toContain('LIVE-');
      }
      const shared = await request(client, `/api/invoices/${live.shared}/xml`, own); expect(shared.status).toBe(200); expect(shared.text).toContain(client === page ? 'LIVE-HI' : 'LIVE-B-SHARED');
    }
    for (const format of ['xml', 'pdf']) expect((await request(page, `/api/invoices/${live.foreign}/${format}`, live.tenants[0])).status).toBe(404);
    expect((await request(page, '/api/tenants', undefined, 'GET', undefined, { Authorization: 'Bearer invalid' })).status).toBe(401);
    expect((await request(page, '/api/tenants', undefined, 'POST', { name: 'must-not-create', nip: '1111111111', taxEntityName: 'must-not-create' })).status).toBe(400);
    live.revokeMembership();
    for (const path of [query, `/api/invoices/${live.prefix}-SECOND/xml`, `/api/invoices/${live.prefix}-SECOND/pdf`]) expect((await request(page, path, live.tenants[1])).status).toBe(403);
    expect((await request(page, '/api/tenants')).data.map((row: { id: string }) => row.id)).not.toContain(live.tenants[1]);
    await page.getByLabel('Aktywny tenant').selectOption(live.tenants[1]);
    await expect(page.getByLabel('Aktywny tenant').locator('option')).toHaveCount(3);
    await expect(page.getByLabel('Aktywny tenant')).toHaveValue('');
    await expect(page.getByRole('grid')).toHaveCount(0); await expect(page.locator('.session-user')).toHaveText(live.loginA);
    await page.getByLabel('Aktywny tenant').selectOption(live.tenants[0]); await expect(cell(page, 'LIVE-HI')).toBeVisible();
    live.expireA();
    for (const path of [query, `/api/invoices/${live.shared}/xml`, `/api/invoices/${live.shared}/pdf`]) expect((await request(page, path, live.tenants[0])).status).toBe(401);
    await page.getByRole('button', { name: 'Odśwież listę', exact: true }).click(); await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('grid')).toHaveCount(0);
    expect((await request(other, '/api/browser-auth/me')).status).toBe(200);
  } finally { await contextB.close(); }
});

test('shared HTTPS cookie: logout, A to B, visibility and delayed real SQL response never restore A', async ({ page, live }) => {
  await login(page, live.loginA); await page.getByLabel('Aktywny tenant').selectOption(live.tenants[0]); await expect(cell(page, 'LIVE-HI')).toBeVisible();
  const peer = await page.context().newPage();
  await peer.addInitScript(() => {
    const nativeFetch = window.fetch.bind(window);
    let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
    Object.assign(window, { liveRelease: release, liveResponseReady: false, liveHold: false });
    window.fetch = async (input, init) => {
      if (!(window as unknown as { liveHold: boolean }).liveHold || !String(input).includes('/api/invoices/query?')) return nativeFetch(input, init);
      Object.assign(window, { liveHold: false });
      // Real browser HTTPS + API + SQL. Only delivery to React is gated; deliberately ignore abort.
      const response = await nativeFetch(input, { ...init, signal: undefined });
      Object.assign(window, { liveResponseReady: true }); await gate; return response;
    };
  });
  await peer.goto('/app/invoices/sales'); await peer.getByLabel('Aktywny tenant').selectOption(live.tenants[0]); await expect(cell(peer, 'LIVE-HI')).toBeVisible();
  await peer.getByLabel('Szukaj w fakturach').fill('LIVE-HI');
  await peer.locator('.ag-floating-filter[col-id="netAmount"] input').fill('9999999999999999,99');
  await peer.getByRole('row').filter({ has: cell(peer, 'LIVE-HI') }).getByRole('checkbox').check();
  await expect(peer.getByTestId('selected-count')).toHaveText('1');
  await peer.evaluate(() => Object.assign(window, { liveHold: true }));
  const release = () => peer.evaluate(() => (window as unknown as { liveRelease: () => void }).liveRelease());
  try {
    await peer.getByRole('button', { name: 'Odśwież listę', exact: true }).click();
    await expect.poll(() => peer.evaluate(() => (window as unknown as { liveResponseReady: boolean }).liveResponseReady)).toBe(true);
    await page.getByRole('button', { name: 'Wyloguj', exact: true }).click();
    await expect(peer).toHaveURL(/\/login$/); await expect(peer.getByRole('grid')).toHaveCount(0); await expect(peer.locator('.session-user')).toHaveCount(0);
    await peer.evaluate(() => {
      const leaks: string[] = []; Object.assign(window, { liveLeaks: leaks });
      new MutationObserver(records => {
        const text = [document.body.textContent, ...records.flatMap(record => Array.from(record.addedNodes, node => node.textContent))].join(' ');
        if (/LIVE-(?:HI|LO|NEG|ZERO|SMALL|MISSING)|999\s999\s999\s999\s999,99/.test(text)) leaks.push('old invoices or totals');
      }).observe(document.body, { childList: true, subtree: true, characterData: true });
    });
    await login(page, live.loginB); await expect(peer.locator('.session-user')).toHaveText(live.loginB);
    await peer.getByLabel('Aktywny tenant').selectOption(live.tenants[3]); await expect(cell(peer, 'LIVE-B-SHARED')).toBeVisible();
    await expect(peer.getByLabel('Szukaj w fakturach')).toHaveValue('');
    await expect(peer.locator('.ag-floating-filter[col-id="netAmount"] input')).toHaveValue('');
    await expect(peer.getByTestId('selected-count')).toHaveText('0');
    for (const sum of ['net', 'vat', 'gross']) await expect(peer.getByTestId(`summary-${sum}`)).toHaveText('1 665,00');
    await release();
    await peer.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => resolve())));
    await expect(page.locator('.session-user')).toHaveText(live.loginB); await expect(cell(peer, 'LIVE-B-SHARED')).toBeVisible();
    expect(await peer.evaluate(() => (window as unknown as { liveLeaks: string[] }).liveLeaks)).toEqual([]);
    expect((await request(peer, '/api/browser-auth/me')).data.login).toBe(live.loginB);
    live.expireB();
    // Chromium headless keeps both tabs visible; drive the same visibility events as existing E2E tests.
    await peer.evaluate(() => { Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' }); document.dispatchEvent(new Event('visibilitychange')); });
    await page.bringToFront(); await peer.bringToFront();
    await peer.evaluate(() => { Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' }); document.dispatchEvent(new Event('visibilitychange')); });
    await expect(peer).toHaveURL(/\/login$/); await expect(peer.getByRole('grid')).toHaveCount(0);
    for (const format of ['xml', 'pdf']) expect((await request(peer, `/api/invoices/${live.shared}/${format}`, live.tenants[3])).status).toBe(401);
  } finally { if (!peer.isClosed()) { await release(); await peer.close(); } }
});
