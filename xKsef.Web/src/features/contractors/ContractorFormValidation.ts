import type { ContractorRequest } from './ContractorApiContracts';
import { REGISTERED_OFFICE } from './ContractorFormModel';
import type { ContractorFormData } from './ContractorFormModel';

export function validateContractorForm(form: ContractorFormData): string | null {
  if (!form.fullName.trim()) return 'Podaj pełną nazwę kontrahenta.';
  if (!form.shortName.trim()) return 'Podaj skróconą nazwę kontrahenta.';
  if (form.fullName.trim().length > 500) return 'Nazwa pełna nie może przekraczać 500 znaków.';
  if (form.shortName.trim().length > 100) return 'Nazwa skrócona nie może przekraczać 100 znaków.';

  const identifierError = validateContractorIdentifiers(form);
  if (identifierError) return identifierError;

  if (form.euVatNumber.trim().length > 32) return 'Numer VAT UE nie może przekraczać 32 znaków.';
  if (form.isEuVatRegistered && !form.euVatNumber.trim()) return 'Numer VAT UE jest wymagany przy rejestracji do VAT UE.';
  if (!form.isEuVatRegistered && form.euVatNumber.trim()) return 'Numer VAT UE musi być pusty, gdy kontrahent nie jest zarejestrowany do VAT UE.';
  if (form.otherTaxNumber.trim().length > 100) return 'Inny numer podatkowy nie może przekraczać 100 znaków.';

  if (form.addresses.length > 1) return 'Typ adresu nie może się powtarzać.';
  for (const address of form.addresses) {
    if (address.addressType !== REGISTERED_OFFICE) return 'Nieprawidłowy typ adresu.';
    if (address.countryCode.trim().length !== 2) return 'Kod kraju musi zawierać 2 znaki.';
    if (!address.city.trim()) return 'Podaj miejscowość.';
    if (address.city.trim().length > 200) return 'Miejscowość nie może przekraczać 200 znaków.';
    if (address.street.trim().length > 200) return 'Ulica nie może przekraczać 200 znaków.';
    if ([address.buildingNumber, address.apartmentNumber, address.postalCode].some(value => value.trim().length > 20)) return 'Numer budynku, lokalu i kod pocztowy nie mogą przekraczać 20 znaków.';
  }

  return null;
}

export function toContractorRequest(form: ContractorFormData): ContractorRequest {
  const nullable = (value: string): string | null => value.trim() || null;

  return {
    fullName: form.fullName.trim(),
    shortName: form.shortName.trim(),
    nip: nullable(form.nip),
    regon: nullable(form.regon),
    krsNumber: nullable(form.krsNumber),
    euVatNumber: nullable(form.euVatNumber),
    otherTaxNumber: nullable(form.otherTaxNumber),
    isEuVatRegistered: form.isEuVatRegistered,
    isActive: form.isActive,
    addresses: form.addresses.map(address => ({
      addressType: address.addressType,
      countryCode: address.countryCode.trim().toUpperCase(),
      street: address.street.trim(),
      buildingNumber: address.buildingNumber.trim(),
      apartmentNumber: nullable(address.apartmentNumber),
      postalCode: address.postalCode.trim(),
      city: address.city.trim(),
    })),
  };
}

function normalizeNip(value: string): string {
  return value.replace(/[ -]/g, '');
}

function remainder(value: string, weights: readonly number[]): number {
  return weights.reduce((sum, weight, index) => sum + Number(value[index]) * weight, 0) % 11;
}

export function validateContractorIdentifiers(form: Pick<ContractorFormData, 'nip' | 'regon' | 'krsNumber'>): string | null {
  const nip = form.nip.trim();
  if (nip && (!/^[0-9 -]+$/.test(nip) || !/^\d{10}$/.test(normalizeNip(nip)))) return 'NIP musi zawierać 10 cyfr. Dozwolone separatory: spacja i myślnik.';
  if (nip && remainder(normalizeNip(nip), [6, 5, 7, 2, 3, 4, 5, 6, 7]) !== Number(normalizeNip(nip)[9])) return 'Nieprawidłowa suma kontrolna NIP.';
  if (form.regon.trim() && !/^(\d{9}|\d{14})$/.test(form.regon.trim())) return 'REGON musi zawierać 9 lub 14 cyfr.';
  const regon = form.regon.trim();
  if (regon && remainder(regon, [8, 9, 2, 3, 4, 5, 6, 7]) % 10 !== Number(regon[8])) return 'Nieprawidłowa suma kontrolna REGON.';
  if (regon.length === 14 && remainder(regon, [2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8]) % 10 !== Number(regon[13])) return 'Nieprawidłowa suma kontrolna REGON jednostki lokalnej.';
  if (form.krsNumber.trim() && !/^\d{10}$/.test(form.krsNumber.trim())) return 'KRS musi zawierać 10 cyfr.';

  return null;
}
