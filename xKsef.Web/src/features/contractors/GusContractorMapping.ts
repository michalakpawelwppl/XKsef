import type { GusContractorDto } from './ContractorApiContracts';
import { REGISTERED_OFFICE } from './ContractorFormModel';
import type { ContractorFormData } from './ContractorFormModel';

/** Applies the registry's complete address, including missing values, without mixing old address fragments. */
export function applyGusContractor(form: ContractorFormData, result: GusContractorDto): ContractorFormData {
  const text = (value: string | null) => value?.trim() ?? '';
  const address = {
    addressType: REGISTERED_OFFICE, countryCode: 'PL',
    street: text(result.address.street), buildingNumber: text(result.address.buildingNumber),
    apartmentNumber: text(result.address.apartmentNumber), postalCode: text(result.address.postalCode), city: text(result.address.city),
  };
  const hasOffice = form.addresses.some(item => item.addressType === REGISTERED_OFFICE);
  const hasAddress = [address.street, address.buildingNumber, address.apartmentNumber, address.postalCode, address.city].some(Boolean);
  return {
    ...form, fullName: text(result.fullName), nip: text(result.nip), regon: text(result.regon),
    // Basic BIR results do not supply KRS. Never clear or infer it from the search criterion.
    krsNumber: result.krs?.trim() || form.krsNumber,
    addresses: hasOffice
      ? form.addresses.map(item => item.addressType === REGISTERED_OFFICE ? address : { ...item })
      : [...form.addresses.map(item => ({ ...item })), ...(hasAddress ? [address] : [])],
  };
}
