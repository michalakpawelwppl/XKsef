import { describe, expect, it } from 'vitest';
import { ApiError } from '../../platform/api/ApiError';
import { readContractor, readContractors } from './ContractorApiContracts';

const contractor = {
  id: 'cccccccc-cccc-cccc-cccc-cccccccccccc', isActive: false, createdAt: '2026-11-22T12:00:00Z',
  fullName: 'Firma', shortName: 'F', nip: null, regon: null, krsNumber: null,
  euVatNumber: null, otherTaxNumber: null, isEuVatRegistered: false,
  addresses: [{ addressType: 1, countryCode: 'PL', street: '', buildingNumber: '', apartmentNumber: null, postalCode: '', city: 'Poznań' }],
};
describe('single contractor parser', () => {
  it('uses the same contract as the list parser and copies nested objects', () => {
    const parsed = readContractor(contractor);
    expect(parsed).toEqual(contractor); expect(readContractors([contractor])).toEqual([parsed]);
    expect(parsed).not.toBe(contractor); expect(parsed.addresses[0]).not.toBe(contractor.addresses[0]);
  });
  it('accepts all identification fields and empty addresses', () => {
    const dto = { ...contractor, nip: '8567346215', regon: '123456785', krsNumber: '0000123456', euVatNumber: 'PL8567346215', otherTaxNumber: 'OTHER', isEuVatRegistered: true, addresses: [] };
    expect(readContractor(dto)).toEqual(dto);
  });
  it.each([null, [], {}, { ...contractor, id: '' }, { ...contractor, isActive: null }, { ...contractor, nip: 123 },
    { ...contractor, shortName: undefined }, { ...contractor, addresses: null }, { ...contractor, addresses: [null] },
    { ...contractor, addresses: [{ ...contractor.addresses[0], addressType: '1' }] },
    { ...contractor, addresses: [{ ...contractor.addresses[0], city: null }] },
  ])('rejects malformed DTO %j', value => {
    expect(() => readContractor(value)).toThrow(ApiError);
    expect(() => readContractors([value])).toThrow(ApiError);
  });
});
