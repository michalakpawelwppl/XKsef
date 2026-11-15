import { test as base, expect, type BrowserContext, type Page } from '@playwright/test';
import { withLiveData } from './livePreflight';

export const password = 'M188-Synthetic-Password-123!';
export { LiveData } from './liveData';
import { LiveData } from './liveData';

// Use Chromium's trusted HTTPS transport, without disabling TLS verification.
export async function request(page: Page, path: string, tenant?: string, method = 'GET', body?: unknown, headers: Record<string, string> = {}) {
  return page.evaluate(async args => {
    const response = await fetch(args.path, { method: args.method, credentials: 'same-origin', headers: {
      ...args.headers, ...(args.tenant ? { 'X-Tenant-Id': args.tenant } : {}), ...(args.body === undefined ? {} : { 'Content-Type': 'application/json' }),
    }, body: args.body === undefined ? undefined : JSON.stringify(args.body) });
    const bytes = new Uint8Array(await response.arrayBuffer()); const text = new TextDecoder().decode(bytes); const type = response.headers.get('Content-Type') ?? '';
    return { status: response.status, type, text, data: type.includes('json') && text ? JSON.parse(text) : null, size: bytes.length };
  }, { path, tenant, method, body, headers });
}
export async function login(page: Page, name: string) {
  await page.goto('/login'); await page.getByLabel('Login', { exact: true }).fill(name); await page.getByLabel('Hasło', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Zaloguj', exact: true }).click(); await expect(page.locator('.session-user')).toHaveText(name);
}
export const test = base.extend<{}, { live: LiveData }>({
  live: [async ({ browser }, use) => {
    const data = new LiveData(); let context: BrowserContext | undefined;
    await withLiveData(data, async () => {
      context = await browser.newContext({ baseURL: process.env.XKSEF_WEB_LIVE_ORIGIN, ignoreHTTPSErrors: false }); const page = await context.newPage();
      await page.goto('/register'); await page.getByLabel('Login', { exact: true }).fill(data.loginA); await page.getByLabel('E-mail', { exact: true }).fill(`${data.loginA}@example.invalid`);
      await page.getByLabel('Hasło', { exact: true }).fill(password); await page.getByLabel('Powtórz hasło', { exact: true }).fill(password);
      const registration = page.waitForResponse(response => new URL(response.url()).pathname === '/api/browser-auth/register' && response.request().method() === 'POST');
      await page.getByRole('button', { name: 'Zarejestruj', exact: true }).click();
      expect((await registration).status()).toBe(201); data.recordCreatedUser(data.loginA);
      await expect(page).toHaveURL(/\/login$/);
      const csrf = (await request(page, '/api/browser-auth/csrf')).data.requestToken as string;
      expect((await request(page, '/api/browser-auth/register', undefined, 'POST', { login: data.loginB, email: `${data.loginB}@example.invalid`, password }, { 'X-CSRF-TOKEN': csrf })).status).toBe(201);
      data.recordCreatedUser(data.loginB); data.seed();
    }, () => use(data), async () => { await context?.close(); });
  }, { scope: 'worker' }],
  page: async ({ page, baseURL }, use) => {
    await page.context().route('**/*', async route => {
      const url = new URL(route.request().url()); expect(url.origin).toBe(new URL(baseURL!).origin);
      // Fail closed: tests cannot initiate imports, credentials or KSeF sends.
      if (url.pathname.startsWith('/api/')) expect(url.pathname).toMatch(/^\/api\/(browser-auth\/(?:me|csrf|register|login|logout)|tenants|invoices\/(?:query|[^/]+\/(?:xml|pdf)))$/);
      expect([...url.searchParams.keys()].some(key => /token|password|secret|authorization|cookie/i.test(key))).toBe(false);
      await route.continue();
    }); await use(page);
  },
});
export { expect };
