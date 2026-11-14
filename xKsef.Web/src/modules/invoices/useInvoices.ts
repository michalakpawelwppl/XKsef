import { useEffect, useMemo, useRef, useState } from 'react';
import { errorMessage } from '../../platform/api/ApiError';
import { useSession } from '../../platform/session/SessionProvider';
import { StaleContextError } from '../../platform/session/RequestContext';
import { InvoiceApiClient } from './InvoiceApiClient';
import type { ApiInvoiceRow } from './InvoiceApiClient';
import type { ApiInvoiceDirection, InvoiceQuery } from './InvoiceApiContracts';

export function defaultInvoiceQuery(direction: ApiInvoiceDirection, today = new Date()): InvoiceQuery {
  const from = new Date(today); from.setDate(from.getDate() - 29);
  const date = (value: Date) => `${value.getFullYear().toString().padStart(4, '0')}-${(value.getMonth() + 1).toString().padStart(2, '0')}-${value.getDate().toString().padStart(2, '0')}`;
  return { from: date(from), to: date(today), direction, dateKind: 'IssueDate' };
}

interface InvoiceState {
  readonly key: string;
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly items: readonly ApiInvoiceRow[];
  readonly error: string | null;
}
const noItems: readonly ApiInvoiceRow[] = [];

export function useInvoices(filters: InvoiceQuery) {
  const { api, state: session } = useSession();
  const client = useMemo(() => new InvoiceApiClient(api), [api]);
  const { from, to, direction, dateKind, timeZoneId } = filters;
  const query = useMemo(() => ({ from, to, direction, dateKind, timeZoneId }), [from, to, direction, dateKind, timeZoneId]);
  const [revision, setRevision] = useState(0);
  const request = useRef<AbortController | null>(null);
  const key = JSON.stringify([session.sessionGeneration, session.tenantGeneration, session.activeTenantId, query, revision]);
  const enabled = session.status === 'authenticated' && session.activeTenantId !== null;
  const pending: InvoiceState = { key, status: enabled ? 'loading' : 'idle', items: noItems, error: null };
  const [result, setResult] = useState<InvoiceState>(pending);

  useEffect(() => {
    const controller = new AbortController();
    request.current = controller;
    let current = true;
    setResult({ key, status: enabled ? 'loading' : 'idle', items: noItems, error: null });
    if (enabled) {
      void client.queryInvoices(query, controller.signal).then(response => {
        if (current && !controller.signal.aborted) setResult({ key, status: 'ready', items: response.items, error: null });
      }).catch((error: unknown) => {
        if (!current || controller.signal.aborted || error instanceof StaleContextError || error instanceof Error && error.name === 'AbortError') return;
        setResult({ key, status: 'error', items: noItems, error: errorMessage(error) });
      });
    }
    return () => { current = false; controller.abort(); if (request.current === controller) request.current = null; };
  }, [client, enabled, key, query]);

  // Hide old rows during render, before effect cleanup runs after a context change.
  return { ...(result.key === key && enabled ? result : pending), query, reload: () => {
    // Cancel before React commits new filters, so a late 401/403 cannot affect the session.
    request.current?.abort(); setRevision(value => value + 1);
  } };
}
