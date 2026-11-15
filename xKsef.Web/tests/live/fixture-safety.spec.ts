import { test, expect, type BrowserContext } from '@playwright/test';
import { LiveData } from './liveData';
import { liveRuntime, withLiveData } from './livePreflight';
import { password, request } from './liveFixture';

test('wrong SQL server fails read-only preflight before any registration or SQL preparation', async () => {
  const runtime = liveRuntime(); const data = new LiveData({ ...runtime, env: { ...runtime.env, XKSEF_SQL_TEST_EXPECTED_SERVER: 'deliberately-wrong-live-server' } });
  let prepared = false; let used = false;
  await expect(withLiveData(data, async () => { prepared = true; }, async () => { used = true; }, async () => {})).rejects.toThrow('Live SQL');
  expect(prepared).toBe(false); expect(used).toBe(false); data.cleanup();
});

test('a failed setup after one real registration removes only that run and cleanup is repeatable', async ({ browser, baseURL }) => {
  const data = new LiveData(); let context: BrowserContext | undefined; let used = false;
  await expect(withLiveData(data, async () => {
    context = await browser.newContext({ baseURL, ignoreHTTPSErrors: false }); const page = await context.newPage();
    await page.goto('/login'); const csrf = (await request(page, '/api/browser-auth/csrf')).data.requestToken as string;
    const result = await request(page, '/api/browser-auth/register', undefined, 'POST', { login: data.loginA, email: `${data.loginA}@example.invalid`, password }, { 'X-CSRF-TOKEN': csrf });
    expect(result.status).toBe(201); data.recordCreatedUser(data.loginA);
    throw new Error('Intentional partial setup failure');
  }, async () => { used = true; }, async () => { await context?.close(); })).rejects.toThrow('Intentional partial setup failure');
  expect(used).toBe(false); data.cleanup();
});
