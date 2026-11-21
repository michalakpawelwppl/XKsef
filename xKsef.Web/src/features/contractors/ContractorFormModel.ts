export const REGISTERED_OFFICE = 1;

export interface ContractorFormAddress {
  addressType: number;
  countryCode: string;
  street: string;
  buildingNumber: string;
  apartmentNumber: string;
  postalCode: string;
  city: string;
}

export interface ContractorFormData {
  fullName: string;
  shortName: string;
  nip: string;
  regon: string;
  krsNumber: string;
  euVatNumber: string;
  otherTaxNumber: string;
  isEuVatRegistered: boolean;
  isActive: boolean;
  addresses: ContractorFormAddress[];
}

export function createEmptyContractorForm(): ContractorFormData {
  return {
    fullName: '',
    shortName: '',
    nip: '',
    regon: '',
    krsNumber: '',
    euVatNumber: '',
    otherTaxNumber: '',
    isEuVatRegistered: false,
    isActive: true,
    addresses: [{
      addressType: REGISTERED_OFFICE,
      countryCode: 'PL',
      street: '',
      buildingNumber: '',
      apartmentNumber: '',
      postalCode: '',
      city: '',
    }],
  };
}