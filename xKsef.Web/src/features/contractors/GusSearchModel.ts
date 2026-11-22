import type { GusIdentifierType } from './ContractorApiContracts';
import { validateContractorIdentifiers } from './ContractorFormValidation';

export interface GusSearchFields { nip: string; regon: string; krs: string }

export function gusSearchState(fields: GusSearchFields) {
  const populated = (['nip', 'regon', 'krs'] as const).filter(key => fields[key].length > 0);
  const active: GusIdentifierType | null = populated.length === 1 ? populated[0] : null;
  return { active, canSearch: active !== null, identifier: active ? fields[active].trim() : '' };
}

/** Called on submit only; typing state never validates the identifier. */
export function validateGusSearch(fields: GusSearchFields): string | null {
  const state = gusSearchState(fields);
  if (!state.active) return 'Podaj dokładnie jeden identyfikator.';
  if (!state.identifier) return 'Podaj identyfikator wyszukiwania.';
  return validateContractorIdentifiers({ nip: fields.nip, regon: fields.regon, krsNumber: fields.krs });
}
