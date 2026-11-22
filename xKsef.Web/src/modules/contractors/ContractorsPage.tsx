import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSession } from '../../platform/session/SessionProvider';
import { ContractorApiClient } from '../../features/contractors/ContractorApiClient';
import type { ContractorDto } from '../../features/contractors/ContractorApiContracts';
import { errorMessage } from '../../platform/api/ApiError';
import { StaleContextError } from '../../platform/session/RequestContext';
import type { RequestSnapshot } from '../../platform/session/RequestContext';
import { ContractorTable } from '../../features/contractors/ContractorTable';
import { ContractorDialog } from '../../features/contractors/ContractorDialog';

export function ContractorsPage() {
  const { api, state: session } = useSession();
  const contractorClient = useMemo(() => new ContractorApiClient(api), [api]);
  const [contractors, setContractors] = useState<readonly ContractorDto[]>([]);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [dialog, setDialog] = useState<{ contractorId?: string; context: RequestSnapshot } | null>(null);
  const closeDialog = useCallback(() => setDialog(null), []);
  const saved = useCallback(() => { setDialog(null); setRevision(value => value + 1); }, []);
  const [tableRevision, setTableRevision] = useState(0);

  useEffect(() => {
    if (!session.activeTenantId) {
      return;
    }

    const controller = new AbortController();
    let current = true;

    async function load() {
      setStatus('loading');
      setError(null);

      try {
        const result = await contractorClient.getContractors(controller.signal);
        if (!current) return;
        setContractors(result);
        setStatus('ready');
      } catch (err: unknown) {
        if (!current || controller.signal.aborted || err instanceof StaleContextError) return;
        setError(errorMessage(err));
        setStatus('error');
      }
    }

    void load();

    return () => {
      current = false;
      controller.abort();
    };
  }, [contractorClient, session.activeTenantId, session.sessionGeneration, session.tenantGeneration, revision]);

  if (!session.activeTenantId) return <main><h1>Kontrahenci</h1><p>Wybierz tenanta, aby otworzyć moduł.</p></main>;

  return <main>
    <div className="breadcrumb">Kartoteki <span aria-hidden="true">/</span> Kontrahenci</div>
    <div className="page-heading">
      <div>
        <h1>Kontrahenci</h1>
        {/* <p>Kontrahenci zapisani w bazie xKsef.</p> */}
      </div>
      <button className="secondary-button" disabled={status === 'loading'} onClick={() => setRevision(value => value + 1)}>
        {status === 'error' ? 'Spróbuj ponownie' : 'Odśwież listę'}
      </button>
      <button className="secondary-button" type="button" disabled={session.verification !== 'idle'} onClick={() => setDialog({ context: api.context.capture('tenant') })}>Nowy kontrahent</button>
    </div>

    {status === 'loading' && <p role="status">Wczytywanie kontrahentów…</p>}
    {status === 'error' && <p className="error-message" role="alert">{error}</p>}
    {status === 'ready' && contractors.length === 0 && <p>Brak kontrahentów w kartotece.</p>}
    <ContractorTable
      key={`${session.activeTenantId}:${tableRevision}`}
      contractors={contractors}
      onReset={() => setTableRevision(value => value + 1)}
      busy={status !== 'ready' || session.verification !== 'idle'}
      onEdit={contractor => setDialog({ contractorId: contractor.id, context: api.context.capture('tenant') })}
    />
    {dialog && <ContractorDialog key={dialog.contractorId ?? 'new'} contractorId={dialog.contractorId} openingContext={dialog.context} onClose={closeDialog} onSaved={saved} />}
  </main>;
}
