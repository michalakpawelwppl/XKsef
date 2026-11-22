import { describe, expect, it } from 'vitest';
import { gusSearchState, validateGusSearch } from './GusSearchModel';

describe('GUS search criteria', () => {
  it('starts without an active identifier and returns to that state after clearing', () => {
    const fields = { nip: '', regon: '', krs: '' };
    expect(gusSearchState(fields)).toMatchObject({ active: null, canSearch: false });
    fields.nip = '8'; expect(gusSearchState(fields).active).toBe('nip');
    fields.nip = ''; expect(gusSearchState(fields)).toMatchObject({ active: null, canSearch: false });
  });
  it.each([['nip', '856-734-62-15'], ['regon', '123456785'], ['regon', '12345678512347'], ['krs', '0000123456']] as const)('uses existing validation for valid %s', (key, identifier) => {
    const fields = { nip: '', regon: '', krs: '', [key]: ` ${identifier} ` };
    expect(gusSearchState(fields)).toEqual({ active: key, canSearch: true, identifier });
    expect(validateGusSearch(fields)).toBeNull();
  });
  it.each([['nip', '8567346214'], ['nip', '123'], ['regon', '123456784'], ['regon', '12345678512348'], ['krs', '123'], ['krs', '000012345a'], ['nip', '  ']] as const)('blocks invalid %s %s', (key, identifier) => {
    const fields = { nip: '', regon: '', krs: '', [key]: identifier };
    const state = gusSearchState(fields);
    expect(state.active).toBe(key); expect(state.canSearch).toBe(true); expect(state).not.toHaveProperty('error');
    expect(validateGusSearch(fields)).toBeTruthy();
  });
  it('rejects multiple criteria defensively', () => {
    const fields = { nip: '8567346215', regon: '123456785', krs: '' };
    expect(gusSearchState(fields)).toMatchObject({ canSearch: false });
    expect(validateGusSearch(fields)).toBe('Podaj dokładnie jeden identyfikator.');
  });
});
