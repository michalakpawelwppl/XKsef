import { describe, expect, it } from 'vitest';
import { readContractor } from './ContractorApiContracts';
import { createEmptyContractorForm, toContractorForm } from './ContractorFormModel';
import { toContractorRequest, validateContractorForm } from './ContractorFormValidation';

function stored() {
  return readContractor({
    id: 'cccccccc-cccc-cccc-cccc-cccccccccccc', createdAt: '2026-11-22T12:00:00Z', isActive: false,
    fullName: 'Firma', shortName: 'F', nip: null, regon: null, krsNumber: null, euVatNumber: null,
    otherTaxNumber: null, isEuVatRegistered: false,
    addresses: [{ addressType: 1, countryCode: 'PL', street: '', buildingNumber: '', apartmentNumber: null, postalCode: '', city: 'Poznań' }],
  });
}
function validForm() { return toContractorForm(stored()); }

describe('contractor form mapping', () => {
  it('round-trips every field and preserves inactive status without DTO metadata', () => {
    const dto = stored();
    Object.assign(dto, { nip: '856-734-62-15', regon: '12345678512347', krsNumber: '0000123456', euVatNumber: 'PL8567346215', otherTaxNumber: 'OTHER', isEuVatRegistered: true });
    const { id: _id, createdAt: _createdAt, ...request } = dto;
    expect(toContractorRequest(toContractorForm(dto))).toEqual(request);
  });
  it('creates independent forms, address arrays and address objects', () => {
    const dto = stored(); const first = toContractorForm(dto); const second = toContractorForm(dto);
    first.fullName = 'Zmieniona'; first.addresses[0].city = 'Warszawa'; first.addresses.push({ ...first.addresses[0] });
    expect(dto.fullName).toBe('Firma'); expect(dto.addresses).toHaveLength(1); expect(dto.addresses[0].city).toBe('Poznań');
    expect(second.addresses[0].city).toBe('Poznań');
  });
  it('preserves absent addresses and nullable identifiers without inventing an empty address', () => {
    const dto = stored(); dto.addresses = [];
    const form = toContractorForm(dto);
    expect(form.addresses).toEqual([]); expect(form.nip).toBe(''); expect(validateContractorForm(form)).toBeNull();
    expect(toContractorRequest(form)).toMatchObject({ addresses: [], isActive: false, nip: null, regon: null, krsNumber: null, euVatNumber: null, otherTaxNumber: null });
  });
  it('normalizes request text without mutating the form or removing NIP separators', () => {
    const form = validForm(); Object.assign(form, { fullName: ' Firma ', shortName: ' F ', nip: ' 856-734-62-15 ', regon: ' ', otherTaxNumber: ' OTHER ' });
    Object.assign(form.addresses[0], { countryCode: ' pl ', city: ' Poznań ', street: ' Ulica ', buildingNumber: ' 12 ', apartmentNumber: ' ', postalCode: ' 60-001 ' });
    const before = structuredClone(form);
    expect(toContractorRequest(form)).toMatchObject({ fullName: 'Firma', shortName: 'F', nip: '856-734-62-15', regon: null, otherTaxNumber: 'OTHER', addresses: [{ addressType: 1, countryCode: 'PL', city: 'Poznań', street: 'Ulica', buildingNumber: '12', apartmentNumber: null, postalCode: '60-001' }] });
    expect(form).toEqual(before);
  });
  it('keeps creation defaults and independent registered-office addresses', () => {
    const first = createEmptyContractorForm(); const second = createEmptyContractorForm();
    expect(first.isActive).toBe(true); expect(first.addresses).toHaveLength(1);
    expect(first.addresses[0]).toMatchObject({ addressType: 1, countryCode: 'PL' });
    first.addresses[0].city = 'Poznań'; expect(second.addresses[0].city).toBe('');
  });
});

describe('contractor backend-compatible validation', () => {
  it.each([
    ['fullName', 500], ['shortName', 100], ['otherTaxNumber', 100],
  ] as const)('checks trimmed %s length at %s', (field, limit) => {
    const form = validForm(); form[field] = ` ${'x'.repeat(limit)} `; expect(validateContractorForm(form)).toBeNull();
    form[field] = 'x'.repeat(limit + 1); expect(validateContractorForm(form)).not.toBeNull();
  });
  it.each(['fullName', 'shortName'] as const)('requires %s', field => {
    const form = validForm(); form[field] = ' '; expect(validateContractorForm(form)).not.toBeNull();
  });
  it.each([
    ['nip', '8567346215', true], ['nip', ' 856-734-62-15 ', true], ['nip', '8567346216', false],
    ['nip', '0000000060', false], ['nip', '856734621', false], ['nip', '856.734.62.15', false], ['nip', '８５６７３４６２１５', false],
    ['regon', '123456785', true], ['regon', '12345678512347', true], ['regon', '123456786', false],
    ['regon', '12345678512348', false], ['regon', '12345678612347', false], ['regon', '12345678', false],
    ['krsNumber', '0000123456', true], ['krsNumber', '000012345', false], ['krsNumber', '000012345x', false],
    ['nip', ' ', true], ['regon', '', true], ['krsNumber', ' ', true],
  ] as const)('validates %s=%s (valid=%s)', (field, value, valid) => {
    const form = validForm(); form[field] = value; expect(validateContractorForm(form) === null).toBe(valid);
  });
  it('requires VAT UE exactly when registered and enforces its length', () => {
    const form = validForm(); form.isEuVatRegistered = true;
    expect(validateContractorForm(form)).not.toBeNull();
    form.euVatNumber = ` ${'x'.repeat(32)} `; expect(validateContractorForm(form)).toBeNull();
    form.euVatNumber += 'x'; expect(validateContractorForm(form)).not.toBeNull();
    form.euVatNumber = 'PL8567346215'; form.isEuVatRegistered = false; expect(validateContractorForm(form)).not.toBeNull();
  });
  it.each([['city', 200], ['street', 200], ['buildingNumber', 20], ['apartmentNumber', 20], ['postalCode', 20]] as const)('checks trimmed address %s length', (field, limit) => {
    const form = validForm(); form.addresses[0][field] = ` ${'x'.repeat(limit)} `; expect(validateContractorForm(form)).toBeNull();
    form.addresses[0][field] = 'x'.repeat(limit + 1); expect(validateContractorForm(form)).not.toBeNull();
  });
  it('requires city and a two-character country only when an address exists', () => {
    const form = validForm(); expect(validateContractorForm(form)).toBeNull();
    form.addresses[0].city = ' '; expect(validateContractorForm(form)).not.toBeNull();
    form.addresses[0].city = 'Poznań'; form.addresses[0].countryCode = 'POL'; expect(validateContractorForm(form)).not.toBeNull();
    form.addresses[0].countryCode = 'P'; expect(validateContractorForm(form)).not.toBeNull();
    form.addresses[0].countryCode = '12'; expect(validateContractorForm(form)).toBeNull();
  });
  it('rejects unsupported and duplicate address types', () => {
    const form = validForm(); form.addresses[0].addressType = 2; expect(validateContractorForm(form)).not.toBeNull();
    form.addresses[0].addressType = 1; form.addresses.push({ ...form.addresses[0] }); expect(validateContractorForm(form)).not.toBeNull();
  });
});
