import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { KeyboardEvent } from 'react';
import { useSession } from '../../platform/session/SessionProvider';
import { createEmptyContractorForm, REGISTERED_OFFICE, toContractorForm } from './ContractorFormModel';
import type { ContractorFormAddress } from './ContractorFormModel';
import { ContractorApiClient } from './ContractorApiClient';
import { validateContractorForm, toContractorRequest } from './ContractorFormValidation';
import { ApiError, errorMessage } from '../../platform/api/ApiError';
import { StaleContextError } from '../../platform/session/RequestContext';
import type { RequestSnapshot } from '../../platform/session/RequestContext';

interface Props {
  readonly onClose: () => void;
  readonly onSaved: () => void;
  readonly contractorId?: string;
  readonly openingContext: RequestSnapshot;
}

function failureMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 404) return 'Kontrahent nie istnieje lub nie jest dostępny w wybranym tenancie.';
  if (error instanceof ApiError && error.validation.length) return error.validation.join(' ');
  return errorMessage(error);
}

export function ContractorDialog({ onClose, onSaved, contractorId, openingContext }: Props) {
  const { api, state: session } = useSession();
  const contractorClient = useMemo(() => new ContractorApiClient(api), [api]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const saveControllerRef = useRef<AbortController | null>(null);
  const savingRef = useRef(false);
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const [form, setForm] = useState(createEmptyContractorForm);
  const [loadStatus, setLoadStatus] = useState<'loading' | 'ready' | 'error'>(contractorId ? 'loading' : 'ready');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [loadRevision, setLoadRevision] = useState(0);
  const address = form.addresses[0];
  const blocked = saving || loadStatus !== 'ready' || notFound || session.verification !== 'idle';

  useEffect(() => {
    if (!contractorId) return;
    const id = contractorId;
    const controller = new AbortController();
    setLoadStatus('loading');
    setLoadError(null);
    async function load() {
      try {
        api.context.assertCurrent(openingContext);
        const result = await contractorClient.getContractor(id, controller.signal);
        api.context.assertCurrent(openingContext);
        if (controller.signal.aborted) return;
        setForm(toContractorForm(result));
        setLoadStatus('ready');
      } catch (error) {
        if (controller.signal.aborted || !api.context.isCurrent(openingContext) || error instanceof StaleContextError) return;
        setNotFound(error instanceof ApiError && error.status === 404);
        setLoadError(failureMessage(error));
        setLoadStatus('error');
      }
    }
    void load();
    return () => controller.abort();
  }, [api, contractorClient, contractorId, openingContext, loadRevision]);

  useEffect(() => {
    const unsubscribe = api.context.subscribe(() => {
      if (!api.context.isCurrent(openingContext)) {
        saveControllerRef.current?.abort();
        onClose();
      }
    });
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    closeButtonRef.current?.focus();

    if (!api.context.isCurrent(openingContext)) onClose();

    return () => {
      saveControllerRef.current?.abort();
      unsubscribe();
      if (previousFocus?.isConnected) previousFocus.focus();
    };
  }, [api, onClose, openingContext]);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }

    if (event.key !== 'Tab') return;

    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('button:enabled, input:enabled, select:enabled, textarea:enabled, a[href], [tabindex]:not([tabindex="-1"])') ?? []).filter(element => element.getClientRects().length > 0);

    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && (document.activeElement === first || !dialogRef.current?.contains(document.activeElement))) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && (document.activeElement === last || !dialogRef.current?.contains(document.activeElement))) {
      event.preventDefault();
      first.focus();
    }
  }

  function updateField<K extends keyof typeof form>(field: K, value: (typeof form)[K]) {
    setForm(previous => ({ ...previous, [field]: value }));
  }

  function updateAddress<K extends keyof ContractorFormAddress>(field: K, value: ContractorFormAddress[K]) {
    setForm(previous => {
      const current = previous.addresses[0] ?? { addressType: REGISTERED_OFFICE, countryCode: 'PL', street: '', buildingNumber: '', apartmentNumber: '', postalCode: '', city: '' };
      return { ...previous, addresses: [{ ...current, [field]: value }] };
    });
  }

  async function saveContractor() {
    if (savingRef.current || blocked) return;
    if (!api.context.isCurrent(openingContext)) { onClose(); return; }

    setSaveError(null);

    const validationError = validateContractorForm(form);
    if (validationError) {
      setSaveError(validationError);
      return;
    }

    const controller = new AbortController();
    saveControllerRef.current = controller;
    savingRef.current = true;
    setSaving(true);

    try {
      const request = toContractorRequest(form);

      api.context.assertCurrent(openingContext);
      if (contractorId) await contractorClient.updateContractor(contractorId, request, controller.signal);
      else await contractorClient.createContractor(request, controller.signal);
      api.context.assertCurrent(openingContext);

      if (!controller.signal.aborted) onSaved();
    } catch (error: unknown) {
      if (!controller.signal.aborted && api.context.isCurrent(openingContext) && !(error instanceof StaleContextError) && !(error instanceof Error && error.name === 'AbortError')) {
        setNotFound(error instanceof ApiError && error.status === 404);
        setSaveError(failureMessage(error));
      }
    } finally {
      if (saveControllerRef.current === controller) saveControllerRef.current = null;
      savingRef.current = false;
      if (!controller.signal.aborted) setSaving(false);
    }
  }

  return createPortal(
    <div className="contractor-dialog-overlay">
      <div ref={dialogRef} className="contractor-dialog" role="dialog" aria-modal="true" aria-labelledby="contractor-dialog-title" onKeyDown={handleKeyDown} tabIndex={-1}>
        <h2 id="contractor-dialog-title">{contractorId ? 'Edycja kontrahenta' : 'Nowy kontrahent'}</h2>

<div className="contractor-dialog-content">
{loadStatus === 'loading' && <p role="status">Wczytywanie danych kontrahenta…</p>}
{loadStatus === 'error' && <><p className="error-message" role="alert">{loadError}</p>{!notFound && <button className="secondary-button" disabled={session.verification !== 'idle'} onClick={() => setLoadRevision(value => value + 1)}>Spróbuj ponownie</button>}</>}
{loadStatus === 'ready' && <fieldset className="contractor-form" disabled={blocked}>
  <h3 className="contractor-form-section">Dane kontrahenta</h3>

  <label className="contractor-form-full">
    Nazwa pełna *
    <input type="text" value={form.fullName} onChange={event => updateField('fullName', event.target.value)} />
  </label>

  <label>
    Nazwa skrócona *
    <input type="text" value={form.shortName} onChange={event => updateField('shortName', event.target.value)} />
  </label>

  <label>
    NIP
    <input type="text" value={form.nip} onChange={event => updateField('nip', event.target.value)} />
  </label>

  <label>
    REGON
    <input type="text" value={form.regon} onChange={event => updateField('regon', event.target.value)} />
  </label>

  <label>
    KRS
    <input type="text" value={form.krsNumber} onChange={event => updateField('krsNumber', event.target.value)} />
  </label>

  <label>
    Numer VAT UE
    <input type="text" value={form.euVatNumber} onChange={event => updateField('euVatNumber', event.target.value)} />
  </label>

  <label>
    Inny numer identyfikacji podatkowej
    <input type="text" value={form.otherTaxNumber} onChange={event => updateField('otherTaxNumber', event.target.value)} />
  </label>

  <label className="contractor-form-checkbox">
    <input type="checkbox" checked={form.isEuVatRegistered} onChange={event => updateField('isEuVatRegistered', event.target.checked)} />
    Zarejestrowany jako podatnik VAT UE
  </label>

  <h3 className="contractor-form-section">Adres siedziby</h3>
  {!address && <p className="contractor-form-full">Brak adresu siedziby. Uzupełnienie pól doda adres przy zapisie.</p>}

  <label>
    Kraj
    <input type="text" maxLength={2} value={address?.countryCode ?? ''} onChange={event => updateAddress('countryCode', event.target.value.toUpperCase())} />
  </label>

  <label>
    Kod pocztowy
    <input type="text" value={address?.postalCode ?? ''} onChange={event => updateAddress('postalCode', event.target.value)} />
  </label>

  <label>
    Miejscowość
    <input type="text" value={address?.city ?? ''} onChange={event => updateAddress('city', event.target.value)} />
  </label>

  <label>
    Ulica
    <input type="text" value={address?.street ?? ''} onChange={event => updateAddress('street', event.target.value)} />
  </label>

  <label>
    Numer budynku
    <input type="text" value={address?.buildingNumber ?? ''} onChange={event => updateAddress('buildingNumber', event.target.value)} />
  </label>

  <label>
    Numer lokalu
    <input type="text" value={address?.apartmentNumber ?? ''} onChange={event => updateAddress('apartmentNumber', event.target.value)} />
  </label>
</fieldset>}
</div>

<div className="contractor-form-actions">
{saveError && <p className="error-message" role="alert">{saveError}</p>}
<button ref={closeButtonRef} className="secondary-button" type="button" onClick={onClose}>Anuluj</button>
<button className="secondary-button" type="button" disabled={blocked} onClick={() => void saveContractor()}>{saving ? 'Zapisywanie…' : 'Zapisz'}</button>
</div>
      </div>
    </div>,
    document.body
  );
}
