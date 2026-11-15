import { test as base, expect } from '@playwright/test';
import type { BrowserContext, Page } from '@playwright/test';
import { result, send } from './invoiceApi';

export const tenantA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
export const tenantB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const cookieName = 'xksef-test-session';

export interface SessionRequest { path: string; owner: string | null; tenant: string | undefined; status?: number }
export interface SharedSessionApi {
  requests: SessionRequest[];
  revokeAll(): void;
  holdNextQuery(recheckAuthentication: boolean, status?: number): { arrived: Promise<void>; release(): void };
  holdNextIdentity(page: Page): { arrived: Promise<void>; release(result?: 'normal' | 'network' | 503): void };
}

async function mockSharedSession(context: BrowserContext): Promise<SharedSessionApi> {
  const sessions = new Map<string, string>(); let sequence = 0;
  const requests: SessionRequest[] = [];
  let nextQuery: { recheck: boolean; status: number; arrived(): void; released: Promise<void> } | null = null;
  const identities = new Map<Page, { arrived(): void; released: Promise<'normal' | 'network' | 503> }>();
  await context.addInitScript(() => {
    const NativeChannel = window.BroadcastChannel;
    const sent: unknown[] = []; (window as unknown as { sessionSignals: unknown[] }).sessionSignals = sent;
    window.BroadcastChannel = class extends NativeChannel {
      postMessage(message: unknown) { if (this.name === 'xksef.session') sent.push(message); super.postMessage(message); }
    };
  });
  await context.route('**/api/**', async route => {
    const request = route.request(); const headers = await request.allHeaders();
    expect(headers.authorization).toBeUndefined();
    const key = headers.cookie?.split(';').map(value => value.trim()).find(value => value.startsWith(cookieName + '='))?.slice(cookieName.length + 1);
    const owner = key ? sessions.get(key) ?? null : null;
    const path = new URL(request.url()).pathname;
    const entry: SessionRequest = { path, owner, tenant: headers['x-tenant-id'] }; requests.push(entry);
    const reply = async (body: unknown, status = 200, extraHeaders = {}) => { entry.status = status; await send(route, body, status, extraHeaders); };
    if (path === '/api/browser-auth/csrf') return reply({ requestToken: 'fixture-token', headerName: 'X-CSRF-TOKEN' });
    if (path === '/api/browser-auth/login') {
      expect(request.method()).toBe('POST'); expect(headers['x-csrf-token']).toBe('fixture-token');
      const login = (request.postDataJSON() as { login: string }).login; expect(['user-A', 'user-B']).toContain(login);
      if (key) sessions.delete(key);
      const newKey = `synthetic-session-${++sequence}`; sessions.set(newKey, login);
      // A real browser cookie shared by all pages. This HTTP fixture does not test production cookie attributes or cryptography.
      return reply({ userId: login, login }, 200, { 'Set-Cookie': `${cookieName}=${newKey}; Path=/; HttpOnly; SameSite=Lax` });
    }
    if (path === '/api/browser-auth/logout') {
      expect(request.method()).toBe('POST'); expect(headers['x-csrf-token']).toBe('fixture-token');
      if (key) sessions.delete(key);
      return reply({}, owner ? 204 : 401, { 'Set-Cookie': `${cookieName}=; Path=/; Max-Age=0; HttpOnly; SameSite=Lax` });
    }
    expect(request.method()).toBe('GET');
    if (path === '/api/browser-auth/me') {
      const pending = identities.get(request.frame().page());
      if (pending) {
        identities.delete(request.frame().page()); pending.arrived(); const outcome = await pending.released;
        if (outcome === 'network') { entry.status = 0; await route.abort('failed'); return; }
        if (outcome === 503) return reply({}, 503);
      }
      return owner ? reply({ userId: owner, login: owner }) : reply({}, 401);
    }
    if (!owner) return reply({}, 401);
    const tenant = owner === 'user-A' ? tenantA : tenantB;
    if (path === '/api/tenants') return reply([{ id: tenant, name: `Firma ${owner}`, role: 'User' }]);
    expect(path).toBe('/api/invoices/query');
    if (headers['x-tenant-id'] !== tenant) return reply({}, 403);
    const pending = nextQuery; nextQuery = null;
    if (pending) {
      // Capture authorization before holding the reply, matching a request already admitted by API middleware.
      const admittedBody = result(owner === 'user-A' ? 'LATE-A' : 'LATE-B');
      pending.arrived(); await pending.released;
      if (pending.recheck && (!key || !sessions.has(key))) return reply({}, 401);
      return reply(admittedBody, pending.status);
    }
    return reply(result(owner === 'user-A' ? 'OWNER-A' : 'OWNER-B'));
  });
  return {
    requests,
    revokeAll() { sessions.clear(); },
    holdNextQuery(recheckAuthentication, status = 200) {
      expect(nextQuery).toBeNull();
      let arrived!: () => void; let release!: () => void;
      const admitted = new Promise<void>(resolve => { arrived = resolve; });
      const released = new Promise<void>(resolve => { release = resolve; });
      nextQuery = { recheck: recheckAuthentication, status, arrived, released };
      return { arrived: admitted, release };
    },
    holdNextIdentity(page) {
      expect(identities.has(page)).toBe(false);
      let arrived!: () => void; let release!: (outcome: 'normal' | 'network' | 503) => void;
      const admitted = new Promise<void>(resolve => { arrived = resolve; });
      const released = new Promise<'normal' | 'network' | 503>(resolve => { release = resolve; });
      identities.set(page, { arrived, released });
      return { arrived: admitted, release: (outcome = 'normal') => release(outcome) };
    },
  };
}

export const test = base.extend<{ sessionApi: SharedSessionApi }>({
  sessionApi: async ({ context }, use) => { await use(await mockSharedSession(context)); },
});

export async function loginUser(page: Page, login: 'user-A' | 'user-B') {
  await page.getByLabel('Login', { exact: true }).fill(login);
  await page.getByLabel('Hasło', { exact: true }).fill('Synthetic-Password-123!');
  await page.getByRole('button', { name: 'Zaloguj', exact: true }).click();
  await expect(page.locator('.session-user')).toHaveText(login);
}

export async function openSharedTabs(page: Page, api: SharedSessionApi) {
  await page.goto('/login'); await loginUser(page, 'user-A');
  await expect(page.getByRole('gridcell', { name: 'OWNER-A-0', exact: true })).toBeVisible();
  const other = await page.context().newPage();
  expect(other.context()).toBe(page.context());
  await other.goto('/app/invoices/sales');
  await expect(other.getByRole('gridcell', { name: 'OWNER-A-0', exact: true })).toBeVisible();
  expect(api.requests.filter(request => request.path === '/api/browser-auth/login')).toHaveLength(1);
  const cookies = await page.context().cookies(); const sessionCookie = cookies.find(cookie => cookie.name === cookieName);
  expect(sessionCookie?.httpOnly).toBe(true);
  expect(await other.evaluate(() => document.cookie)).not.toContain(cookieName);
  return other;
}
