import { describe, expect, it } from 'vitest';
import { applyGusContractor } from './GusContractorMapping';
import { createEmptyContractorForm } from './ContractorFormModel';
import type { GusContractorDto } from './ContractorApiContracts';
import { validateContractorIdentifiers } from './ContractorFormValidation';

const result: GusContractorDto = {
  fullName: ' Firma GUS ', nip: '8567346215', regon: '123456785', krs: null, shortName: null,
  address: { voivodeship: null, county: null, municipality: null, postOfficeCity: 'Poczta', city: ' Warszawa ', street: ' Nowa ', buildingNumber: '2', apartmentNumber: '3', postalCode: '00-001' },
};
describe('applying a GUS proposal', () => {
  it('maps all supplied fields, uses PL and does not mutate the original', () => {
    const form = createEmptyContractorForm();
    form.addresses[0].countryCode = 'DE';
    const before = structuredClone(form);
    expect(applyGusContractor(form, { ...result, krs: '0000123456' })).toMatchObject({ fullName: 'Firma GUS', nip: '8567346215', regon: '123456785', krsNumber: '0000123456', addresses: [{ addressType: 1, countryCode: 'PL', city: 'Warszawa', street: 'Nowa', buildingNumber: '2', apartmentNumber: '3', postalCode: '00-001' }] });
    expect(form).toEqual(before);
  });
  it.each([null, '', '   '])('clears old mapped values for empty registry fields (%s)', empty => {
    const form = createEmptyContractorForm();
    Object.assign(form.addresses[0], { street: 'Old', city: 'Old', buildingNumber: '1', apartmentNumber: '2', postalCode: 'Old' });
    form.nip = 'Old';
    const mapped = applyGusContractor(form, { ...result, nip: empty, address: { ...result.address, street: empty, city: empty, buildingNumber: empty, apartmentNumber: empty, postalCode: empty } });
    expect(mapped.nip).toBe('');
    expect(mapped.addresses).toEqual([{ addressType: 1, countryCode: 'PL', street: '', city: '', buildingNumber: '', apartmentNumber: '', postalCode: '' }]);
  });
  it('preserves fields outside mapping and other address types', () => {
    const form = createEmptyContractorForm();
    Object.assign(form, { shortName: 'KEEP', isActive: false, isEuVatRegistered: true, euVatNumber: 'PL123', otherTaxNumber: 'OTHER', krsNumber: '0000123456' });
    const other = { ...form.addresses[0], addressType: 2, city: 'Berlin' };
    form.addresses.push(other);
    const mapped = applyGusContractor(form, result);
    for (const key of ['shortName', 'isActive', 'isEuVatRegistered', 'euVatNumber', 'otherTaxNumber', 'krsNumber'] as const) expect(mapped[key]).toEqual(form[key]);
    expect(mapped.addresses[1]).toEqual(other);
  });
  it('creates an incomplete address without substituting the post office city', () => {
    const form = createEmptyContractorForm(); form.addresses = [];
    const mapped = applyGusContractor(form, { ...result, address: { ...result.address, city: null } });
    expect(mapped.addresses[0]).toMatchObject({ city: '', countryCode: 'PL', street: 'Nowa' });
  });
  it('does not invent an address if no address data were returned', () => {
    const form = createEmptyContractorForm(); form.addresses = [];
    const address = Object.fromEntries(Object.keys(result.address).map(key => [key, null])) as unknown as GusContractorDto['address'];
    expect(applyGusContractor(form, { ...result, address }).addresses).toEqual([]);
  });
  it('uses the existing identifier validation independently of required form fields', () => {
    expect(validateContractorIdentifiers({ nip: '856-734-62-15', regon: '', krsNumber: '' })).toBeNull();
    expect(validateContractorIdentifiers({ nip: '8567346214', regon: '', krsNumber: '' })).toContain('suma kontrolna');
    expect(validateContractorIdentifiers({ nip: '', regon: '123456785', krsNumber: '0000123456' })).toBeNull();
  });
});
