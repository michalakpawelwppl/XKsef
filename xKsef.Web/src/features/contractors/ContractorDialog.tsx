import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import type { KeyboardEvent } from 'react';
import { useSession } from '../../platform/session/SessionProvider';
import { useState } from 'react';
import { createEmptyContractorForm } from './ContractorFormModel';
import type { ContractorFormAddress } from './ContractorFormModel';
import { useMemo } from 'react';
import { ContractorApiClient } from './ContractorApiClient';
import { validateContractorForm, toContractorRequest } from './ContractorFormValidation';
import { errorMessage } from '../../platform/api/ApiError';
import { StaleContextError } from '../../platform/session/RequestContext';

interface Props {
  readonly onClose: () => void;
  readonly onSaved: () => void;
}

export function ContractorDialog({ onClose, onSaved }: Props) {
  const { api } = useSession();
  const contractorClient = useMemo(() => new ContractorApiClient(api), [api]);
const [saving, setSaving] = useState(false);
const [saveError, setSaveError] = useState<string | null>(null);
const saveControllerRef = useRef<AbortController | null>(null);
const savingRef = useRef(false);
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
const [form, setForm] = useState(createEmptyContractorForm);

  useEffect(() => {
    const context = api.context.capture('tenant');
const unsubscribe = api.context.subscribe(() => {
  if (!api.context.isCurrent(context)) onClose();
});
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    closeButtonRef.current?.focus();

    if (!api.context.isCurrent(context)) onClose();

    return () => {
  saveControllerRef.current?.abort();
  unsubscribe();
  if (previousFocus?.isConnected) previousFocus.focus();
};
  }, [api, onClose]);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      onClose();
      return;
    }

    if (event.key !== 'Tab') return;

    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])') ?? []).filter(element => element.getClientRects().length > 0);

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
  setForm(previous => ({ ...previous, addresses: previous.addresses.map((address, index) => index === 0 ? { ...address, [field]: value } : address) }));
}

async function saveContractor() {
  if (savingRef.current) return;

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
    const context = api.context.capture('tenant');
    const request = toContractorRequest(form);

    api.context.assertCurrent(context);
    await contractorClient.createContractor(request, controller.signal);
    api.context.assertCurrent(context);

    if (!controller.signal.aborted) onSaved();
  } catch (error: unknown) {
    if (!controller.signal.aborted && !(error instanceof StaleContextError) && !(error instanceof Error && error.name === 'AbortError')) {
      setSaveError(errorMessage(error));
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
        <h2 id="contractor-dialog-title">Nowy kontrahent</h2>

<div className="contractor-dialog-content">
<div className="contractor-form">
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

  <label>
    Kraj
    <input type="text" maxLength={2} value={form.addresses[0].countryCode} onChange={event => updateAddress('countryCode', event.target.value.toUpperCase())} />
  </label>

  <label>
    Kod pocztowy
    <input type="text" value={form.addresses[0].postalCode} onChange={event => updateAddress('postalCode', event.target.value)} />
  </label>

  <label>
    Miejscowość
    <input type="text" value={form.addresses[0].city} onChange={event => updateAddress('city', event.target.value)} />
  </label>

  <label>
    Ulica
    <input type="text" value={form.addresses[0].street} onChange={event => updateAddress('street', event.target.value)} />
  </label>

  <label>
    Numer budynku
    <input type="text" value={form.addresses[0].buildingNumber} onChange={event => updateAddress('buildingNumber', event.target.value)} />
  </label>

  <label>
    Numer lokalu
    <input type="text" value={form.addresses[0].apartmentNumber} onChange={event => updateAddress('apartmentNumber', event.target.value)} />
  </label>
</div>
</div>

<div className="contractor-form-actions">
{saveError && <p className="error-message" role="alert">{saveError}</p>}
<button ref={closeButtonRef} className="secondary-button" type="button" onClick={onClose}>Anuluj</button>
<button className="secondary-button" type="button" disabled={saving} onClick={() => void saveContractor()}>{saving ? 'Zapisywanie…' : 'Zapisz'}</button>
</div>
      </div>
    </div>,
    document.body
  );
}