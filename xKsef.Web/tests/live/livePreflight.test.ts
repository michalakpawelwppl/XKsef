import { describe, expect, it, vi } from 'vitest';
import { LiveData } from './liveData';
import { liveApiDll, liveRuntime, validateLiveManifest, withLiveData } from './livePreflight';

function fixture() {
  const env: Record<string, string | undefined> = { XKSEF_WEB_LIVE_ORIGIN: 'https://localhost:7127', XKSEF_SQL_TEST_EXPECTED_SERVER: 'unit-localdb' };
  const sql = vi.fn((_script: string) => ''); const apiProcess = vi.fn(() => ({ pid: 1234, applicationName: 'xksef-live-' + 'a'.repeat(32) }));
  return { env, sql, apiProcess, data: new LiveData({ env, sql, apiProcess }) };
}
describe('live fixture preflight and cleanup', () => {
  it('requires a dedicated API manifest before reading SQL or registering users', () => {
    expect(() => liveRuntime({}).apiProcess('https://localhost:7127')).toThrow('XKSEF_LIVE_API_MANIFEST');
  });
  it.each([
    { origin: 'https://localhost:7448' }, { database: 'another-database' }, { dataSource: 'another-server' },
    { pid: 0 }, { pid: '1234' }, { applicationName: 'unverified-api' }, { apiDll: 'another-api.dll' },
  ])('rejects a mismatched API manifest before writes (%#)', patch => {
    const manifest = { origin: 'https://localhost:7127', database: 'xKsef', dataSource: '(LocalDb)\\MSSQLLocalDB', pid: 1234, applicationName: 'xksef-live-' + 'a'.repeat(32), apiDll: liveApiDll, ...patch };
    expect(() => validateLiveManifest(manifest, 'https://localhost:7127')).toThrow('manifest');
  });
  it.each([
    { XKSEF_WEB_LIVE_ORIGIN: undefined }, { XKSEF_SQL_TEST_EXPECTED_SERVER: undefined }, { XKSEF_SQL_TEST_EXPECTED_SERVER: ' ' },
    { XKSEF_WEB_LIVE_ORIGIN: 'http://localhost:7127' }, { XKSEF_WEB_LIVE_ORIGIN: 'https://example.invalid' },
    { XKSEF_WEB_LIVE_ORIGIN: 'https://localhost:7127/path' }, { XKSEF_WEB_LIVE_ORIGIN: 'https://localhost:7127/?token=x' },
    { XKSEF_WEB_LIVE_ORIGIN: 'https://user:pass@localhost:7127' },
  ])('rejects invalid configuration before SQL or API writes (%#)', async patch => {
    const f = fixture(); Object.assign(f.env, patch); const prepare = vi.fn(); const use = vi.fn(); const close = vi.fn();
    await expect(withLiveData(f.data, prepare, use, close)).rejects.toThrow();
    expect(prepare).not.toHaveBeenCalled(); expect(use).not.toHaveBeenCalled(); expect(f.sql).not.toHaveBeenCalled();
  });
  it('stops before registration when the read-only SQL verification fails', async () => {
    const f = fixture(); f.sql.mockImplementation(() => { throw new Error('preflight mismatch'); }); const prepare = vi.fn();
    await expect(withLiveData(f.data, prepare, vi.fn(), vi.fn())).rejects.toThrow('preflight mismatch');
    expect(prepare).not.toHaveBeenCalled(); expect(f.sql).toHaveBeenCalledTimes(1);
    expect(f.sql.mock.calls[0][0]).not.toMatch(/\b(?:INSERT|UPDATE|DELETE)\b/);
  });
  it('checks the dedicated API PID/name, expected SQL server, database, LocalDB and identifier ownership', () => {
    const f = fixture(); f.data.preflight();
    expect(f.apiProcess).toHaveBeenCalledWith('https://localhost:7127');
    const sql = f.sql.mock.calls[0][0];
    for (const fragment of ["DB_NAME()<>N'xKsef'", "SERVERPROPERTY('IsLocalDB')", "SERVERPROPERTY('ServerName')", "N'unit-localdb'", 'sys.dm_exec_sessions', 'host_process_id=1234', 'program_name=', 'database_id=DB_ID()', 'database_id<>DB_ID()', f.data.loginA, f.data.loginB]) expect(sql).toContain(fragment);
  });
  it('does not permit seeding or recording unrelated users without a valid preflight', () => {
    const f = fixture(); expect(() => f.data.seed()).toThrow(); expect(() => f.data.recordCreatedUser(f.data.loginA)).toThrow();
    f.data.preflight(); expect(() => f.data.recordCreatedUser('unrelated-user')).toThrow(); expect(() => f.data.seed()).toThrow();
  });
  it('cleans only a confirmed first registration after a partial API setup failure', async () => {
    const f = fixture(); const close = vi.fn(); const use = vi.fn();
    await expect(withLiveData(f.data, async () => { f.data.recordCreatedUser(f.data.loginA); throw new Error('second registration failed'); }, use, close)).rejects.toThrow('second registration failed');
    expect(close).toHaveBeenCalledOnce(); expect(use).not.toHaveBeenCalled();
    const cleanup = f.sql.mock.calls.at(-1)![0];
    expect(cleanup).toContain(`DELETE AspNetUsers WHERE UserName IN (N'${f.data.loginA}')`);
    expect(cleanup).not.toContain(f.data.loginB); expect(cleanup).not.toContain('UserName LIKE');
  });
  it('cleans confirmed accounts when the SQL preparation transaction fails', async () => {
    const f = fixture(); f.sql.mockImplementation(script => { if (script.includes('INSERT invoices')) throw new Error('seed failure'); return ''; });
    await expect(withLiveData(f.data, async () => { f.data.recordCreatedUser(f.data.loginA); f.data.recordCreatedUser(f.data.loginB); f.data.seed(); }, vi.fn(), vi.fn())).rejects.toThrow('seed failure');
    const cleanup = f.sql.mock.calls.at(-1)![0]; expect(cleanup).toContain('DELETE invoices WHERE TenantId IN');
    expect(cleanup).toContain(f.data.loginA); expect(cleanup).toContain(f.data.loginB); expect(cleanup).toContain('Live cleanup incomplete');
  });
  it('still cleans SQL if closing the browser fails', async () => {
    const f = fixture();
    await expect(withLiveData(f.data, async () => f.data.recordCreatedUser(f.data.loginA), async () => {}, async () => { throw new Error('browser disconnected'); })).rejects.toThrow('browser disconnected');
    expect(f.sql.mock.calls.at(-1)![0]).toContain('DELETE AspNetUsers');
  });
  it('pins the verified server for cleanup and makes successful cleanup idempotent', async () => {
    const f = fixture();
    await withLiveData(f.data, async () => { f.data.recordCreatedUser(f.data.loginA); f.env.XKSEF_SQL_TEST_EXPECTED_SERVER = 'changed'; }, async () => {}, async () => {});
    expect(f.sql.mock.calls.at(-1)![0]).toContain("N'unit-localdb'"); expect(f.sql.mock.calls.at(-1)![0]).not.toContain("N'changed'");
    const calls = f.sql.mock.calls.length; f.data.cleanup(); expect(f.sql).toHaveBeenCalledTimes(calls);
  });
  it('preserves ownership information for a retry if SQL cleanup fails', () => {
    const f = fixture(); f.data.preflight(); f.data.recordCreatedUser(f.data.loginA);
    f.sql.mockImplementationOnce(() => { throw new Error('SQL unavailable'); }); expect(() => f.data.cleanup()).toThrow('SQL unavailable');
    f.data.cleanup(); expect(f.sql.mock.calls.at(-1)![0]).toContain(f.data.loginA);
  });
});
