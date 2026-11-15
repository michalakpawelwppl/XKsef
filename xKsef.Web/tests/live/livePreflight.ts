import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export interface LiveRuntime {
  readonly env: Readonly<Record<string, string | undefined>>;
  sql(script: string): string;
  apiProcess(origin: string): { pid: number; applicationName: string };
}
export const liveApiDll = fileURLToPath(new URL('../../../xKsef.Api/bin/Debug/net10.0/xKsef.Api.dll', import.meta.url));
export function validateLiveManifest(value: unknown, origin: string): { pid: number; applicationName: string } {
  if (!value || typeof value !== 'object') throw new Error('Invalid live API launch manifest.');
  const manifest = value as Record<string, unknown>;
  if (manifest.origin !== origin || manifest.database !== 'xKsef' || manifest.dataSource !== '(LocalDb)\\MSSQLLocalDB'
      || typeof manifest.apiDll !== 'string' || manifest.apiDll.toLowerCase() !== liveApiDll.toLowerCase()
      || typeof manifest.pid !== 'number' || !Number.isSafeInteger(manifest.pid) || manifest.pid <= 0
      || typeof manifest.applicationName !== 'string' || !/^xksef-live-[a-f0-9]{32}$/.test(manifest.applicationName)) throw new Error('Invalid live API launch manifest.');
  return { pid: manifest.pid, applicationName: manifest.applicationName };
}
export function liveRuntime(configuration: LiveRuntime['env'] = process.env): LiveRuntime {
  const env = { ...configuration };
  return {
    env,
    sql: script => {
      try {
        return execFileSync('sqlcmd', ['-S', '(LocalDb)\\MSSQLLocalDB', '-d', 'xKsef', '-E', '-b', '-h', '-1', '-W', '-f', '65001'], { input: `${script}\nGO\n`, encoding: 'utf8', windowsHide: true });
      } catch { throw new Error('Live SQL operation failed; verify the test database, permissions and fixture state.'); }
    },
    apiProcess: origin => {
      const path = env.XKSEF_LIVE_API_MANIFEST;
      if (!path) throw new Error('Start the dedicated live API and set XKSEF_LIVE_API_MANIFEST.');
      const manifest = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
      return validateLiveManifest(manifest, origin);
    },
  };
}
export function liveConfiguration(env: LiveRuntime['env']) {
  if (!env.XKSEF_WEB_LIVE_ORIGIN || !env.XKSEF_SQL_TEST_EXPECTED_SERVER?.trim()) throw new Error('Set XKSEF_WEB_LIVE_ORIGIN and XKSEF_SQL_TEST_EXPECTED_SERVER before preparing any data.');
  const origin = new URL(env.XKSEF_WEB_LIVE_ORIGIN);
  if (origin.protocol !== 'https:' || !['localhost', '127.0.0.1', '[::1]'].includes(origin.hostname)
      || origin.username || origin.password || origin.search || origin.hash || origin.pathname !== '/') throw new Error('Expected a local HTTPS origin without credentials, query or path.');
  return { origin: origin.origin, server: env.XKSEF_SQL_TEST_EXPECTED_SERVER };
}

/** Keep cleanup independent of browser shutdown and run it after partially completed setup. */
export async function withLiveData<T>(data: { preflight(): void; cleanup(): void }, prepare: () => Promise<void>, use: () => Promise<T>, close: () => Promise<void>): Promise<T> {
  data.preflight();
  try { await prepare(); return await use(); }
  finally { try { await close(); } finally { data.cleanup(); } }
}
