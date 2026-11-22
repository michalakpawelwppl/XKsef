import { useEffect, useRef, useState } from 'react';
import type { GusContractorDto } from './ContractorApiContracts';

interface Props {
  readonly results: readonly GusContractorDto[];
  readonly onSelect: (result: GusContractorDto) => void;
  readonly onClose: () => void;
}

export function GusResultsDialog({ results, onSelect, onClose }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  const [selected, setSelected] = useState<number | null>(null);
  useEffect(() => {
    const dialog = ref.current!;
    dialog.showModal();
    return () => dialog.close();
  }, []);
  const empty = results.length === 0;
  return <dialog ref={ref} className="contractor-dialog gus-results-dialog" aria-labelledby="gus-results-title"
    onCancel={event => { event.preventDefault(); onClose(); }} onKeyDown={event => event.stopPropagation()}>
    <h2 id="gus-results-title">{empty ? 'Nie znaleziono kontrahenta' : 'Wybierz podmiot z GUS'}</h2>
    {empty ? <p>W rejestrze GUS REGON nie znaleziono podmiotu dla podanego identyfikatora. Dane w formularzu nie zostały zmienione</p>
      : <div className="contractor-dialog-content"><table className="gus-results-table">
        <thead><tr><th>Wybór</th><th>Nazwa pełna</th><th>NIP</th><th>REGON</th><th>Adres siedziby</th></tr></thead>
        <tbody>{results.map((item, index) => <tr key={index} onClick={() => setSelected(index)} onDoubleClick={() => onSelect(item)} aria-selected={selected === index}>
          <td><input type="radio" name="gus-result" aria-label={`Wybierz ${item.fullName}`} checked={selected === index} onChange={() => setSelected(index)} /></td>
          <td>{item.fullName}</td><td>{item.nip || '—'}</td><td>{item.regon}</td>
          <td>{[item.address.street, [item.address.buildingNumber, item.address.apartmentNumber].filter(Boolean).join('/'), item.address.postalCode, item.address.city].filter(Boolean).join(' ') || 'Brak adresu'}</td>
        </tr>)}</tbody>
      </table></div>}
    <div className="contractor-form-actions">
      <button type="button" className="secondary-button" onClick={onClose}>{empty ? 'OK' : 'Anuluj'}</button>
      {!empty && <button type="button" className="secondary-button" disabled={selected === null} onClick={() => { if (selected !== null) onSelect(results[selected]); }}>Wybierz</button>}
    </div>
  </dialog>;
}
