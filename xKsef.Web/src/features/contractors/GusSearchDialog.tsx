import { useEffect, useRef, useState } from 'react';
import type { GusIdentifierType } from './ContractorApiContracts';
import { gusSearchState, validateGusSearch } from './GusSearchModel';

interface Props {
  readonly loading: boolean;
  readonly error: string | null;
  readonly onSearch: (kind: GusIdentifierType, identifier: string) => void;
  readonly onClose: () => void;
}

export function GusSearchDialog({ loading, error, onSearch, onClose }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  const [fields, setFields] = useState({ nip: '', regon: '', krs: '' });
  const [validationError, setValidationError] = useState<string | null>(null);
  const state = gusSearchState(fields);
  useEffect(() => {
    const dialog = ref.current!;
    dialog.showModal();
    return () => dialog.close();
  }, []);
  return <dialog ref={ref} className="contractor-dialog gus-results-dialog" aria-labelledby="gus-search-title"
    onCancel={event => { event.preventDefault(); onClose(); }} onKeyDown={event => event.stopPropagation()}>
    <h2 id="gus-search-title">Pobierz dane z GUS</h2>
    <form onSubmit={event => {
      event.preventDefault();
      if (loading || !state.canSearch || !state.active) return;
      const failure = validateGusSearch(fields);
      setValidationError(failure);
      if (!failure) onSearch(state.active, state.identifier);
    }}>
      <div className="gus-search-fields">
        {(['nip', 'regon', 'krs'] as const).map(key => <div key={key}><label>{key.toUpperCase()}
          <input type="text" value={fields[key]} disabled={loading || (state.active !== null && state.active !== key)}
            aria-invalid={state.active === key && !!validationError} aria-describedby={state.active === key && validationError ? `gus-search-validation-${key}` : undefined}
            onChange={event => { setValidationError(null); setFields(previous => ({ ...previous, [key]: event.target.value })); }} />
        </label>
          {state.active === key && validationError && <p id={`gus-search-validation-${key}`} className="error-message" role="alert">{validationError}</p>}
        </div>)}
      </div>
      {error && <p className="error-message" role="alert">{error}</p>}
      {loading && <p role="status">Pobieranie danych z GUS…</p>}
      <div className="contractor-form-actions">
        <button type="button" className="secondary-button" onClick={onClose}>Anuluj</button>
        <button type="submit" className="secondary-button" disabled={loading || !state.canSearch}>Pobierz</button>
      </div>
    </form>
  </dialog>;
}
