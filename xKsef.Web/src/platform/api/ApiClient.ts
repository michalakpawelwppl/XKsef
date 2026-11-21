import { RequestContext, StaleContextError } from '../session/RequestContext';
import type { RequestScope, RequestSnapshot } from '../session/RequestContext';
import { ApiError, retryAfterSeconds, statusMessage } from './ApiError';

export type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
interface RequestOptions { readonly scope: RequestScope; readonly method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'; readonly body?: unknown; readonly signal?: AbortSignal }

export class ApiClient {
  onUnauthorized?: () => void;
  onTenantForbidden?: () => void;
  #token: string | null = null;
  #csrfGeneration = 0;
  #csrfAbort = new AbortController();
  #pendingToken: Promise<string> | null = null;

  constructor(readonly context: RequestContext, private readonly fetcher: Fetch = globalThis.fetch.bind(globalThis)) {
    context.subscribe(kind => { if (kind === 'session') this.clearCsrf(); });
  }
  clearCsrf() {
    this.#csrfGeneration++; this.#csrfAbort.abort(); this.#csrfAbort = new AbortController();
    this.#token = null; this.#pendingToken = null;
  }
  async refreshCsrf() { this.clearCsrf(); await this.#csrf(); }
  async #csrf(): Promise<string> {
    if (this.#token) return this.#token;
    if (this.#pendingToken) return this.#pendingToken;
    const snapshot = this.context.capture('anonymous'); const generation = this.#csrfGeneration;
    const promise = (async () => {
      const response = await this.#fetch('/api/browser-auth/csrf', { method: 'GET', signal: AbortSignal.any([snapshot.signal, this.#csrfAbort.signal]) }, snapshot);
      const data = await this.#decode(response, snapshot);
      this.context.assertCurrent(snapshot);
      if (generation !== this.#csrfGeneration) throw new StaleContextError();
      if (!data || typeof data !== 'object' || !('requestToken' in data) || typeof data.requestToken !== 'string' || !data.requestToken
          || !('headerName' in data) || data.headerName !== 'X-CSRF-TOKEN') throw new ApiError(502, 'Nieprawidłowa odpowiedź API dotycząca CSRF.');
      return this.#token = data.requestToken;
    })();
    this.#pendingToken = promise;
    try { return await promise; }
    finally { if (this.#pendingToken === promise) this.#pendingToken = null; }
  }
  async request<T>(path: string | ((snapshot: RequestSnapshot) => string), options: RequestOptions): Promise<T> {
    const snapshot = this.context.capture(options.scope);
    this.#assertCurrent(snapshot, options.signal);
    const method = options.method ?? 'GET';
    const headers = new Headers({ Accept: 'application/json' });
    if (snapshot.tenantId) headers.set('X-Tenant-Id', snapshot.tenantId);
    if (options.body !== undefined) headers.set('Content-Type', 'application/json');
    if (method !== 'GET') headers.set('X-CSRF-TOKEN', await this.#csrf());
    this.#assertCurrent(snapshot, options.signal);
    const response = await this.#fetch(typeof path === 'function' ? path(snapshot) : path, {
      method, headers, body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal ? AbortSignal.any([snapshot.signal, options.signal]) : snapshot.signal,
    }, snapshot);
    return await this.#decode(response, snapshot, options.signal) as T;
  }
async requestBlob(path: string, options: RequestOptions): Promise<Blob> {
  const snapshot = this.context.capture(options.scope);
  this.#assertCurrent(snapshot, options.signal);

  const headers = new Headers({ Accept: 'application/pdf' });
  if (snapshot.tenantId) headers.set('X-Tenant-Id', snapshot.tenantId);

  const response = await this.#fetch(path, {
    method: 'GET',
    headers,
    signal: options.signal ? AbortSignal.any([snapshot.signal, options.signal]) : snapshot.signal,
  }, snapshot);

  if (!response.ok) {
    await this.#decode(response, snapshot, options.signal);
    throw new ApiError(response.status, statusMessage(response.status));
  }

  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.toLowerCase().includes('application/pdf')) throw new ApiError(502, 'API nie zwróciło dokumentu PDF.');

  const blob = await response.blob();
  this.#assertCurrent(snapshot, options.signal);

  return blob;
}

  #assertCurrent(snapshot: RequestSnapshot, signal?: AbortSignal | null) {
    this.context.assertCurrent(snapshot);
    signal?.throwIfAborted();
  }
  async #fetch(path: string, init: RequestInit, snapshot: RequestSnapshot) {
    const url = new URL(path, 'https://same-origin.invalid');
    if (!path.startsWith('/api/') || url.origin !== 'https://same-origin.invalid' || !url.pathname.startsWith('/api/') || url.hash)
      throw new Error('Klient API obsługuje wyłącznie ścieżki /api/ tego samego originu.');
    try {
      const response = await this.fetcher(path, { ...init, credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
      this.#assertCurrent(snapshot, init.signal); return response;
    } catch (error) {
      this.#assertCurrent(snapshot, init.signal);
      if (error instanceof Error && (error.name === 'AbortError' || error instanceof StaleContextError)) throw error;
      throw new ApiError(0, statusMessage(0));
    }
  }
  async #decode(response: Response, snapshot: RequestSnapshot, signal?: AbortSignal): Promise<unknown> {
    let data: unknown;
    if (response.status !== 204) {
      const type = response.headers.get('Content-Type') ?? '';
      if (type.includes('json')) {
        try { data = await response.json(); }
        catch { this.#assertCurrent(snapshot, signal); if (response.ok) throw new ApiError(502, 'Nieprawidłowa odpowiedź API.'); }
      } else if (response.ok) { this.#assertCurrent(snapshot, signal); throw new ApiError(502, 'API zwróciło nieoczekiwany format odpowiedzi.'); }
    }
    this.#assertCurrent(snapshot, signal);
    if (!response.ok) {
      if (response.status === 401 && snapshot.scope !== 'anonymous') this.onUnauthorized?.();
      if (response.status === 403 && snapshot.scope === 'tenant') this.onTenantForbidden?.();
      const errors = response.status === 400 && data && typeof data === 'object' && 'errors' in data && data.errors && typeof data.errors === 'object'
        ? Object.values(data.errors).flat().filter((value): value is string => typeof value === 'string') : [];
      // Only validation messages are presented; SQL details, request bodies and credentials are never logged.
      const csrfRejected = response.status === 400 && data && typeof data === 'object' && 'title' in data
        && typeof data.title === 'string' && data.title.includes('CSRF');
      // Acquire a fresh token only on the next explicit attempt; never replay the failed mutation.
      if (csrfRejected) this.clearCsrf();
      throw new ApiError(response.status, csrfRejected ? 'Nie udało się potwierdzić zabezpieczenia formularza. Spróbuj ponownie.' : statusMessage(response.status), errors, retryAfterSeconds(response.headers.get('Retry-After')));
    }
    return data;
  }
}
