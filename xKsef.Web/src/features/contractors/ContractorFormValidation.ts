import type { ContractorRequest } from './ContractorApiContracts';
import type { ContractorFormData } from './ContractorFormModel';

export function validateContractorForm(form: ContractorFormData): string | null {
  if (!form.fullName.trim()) return 'Podaj pełną nazwę kontrahenta.';
  if (!form.shortName.trim()) return 'Podaj skróconą nazwę kontrahenta.';

  if (form.nip && (!/^[0-9 -]+$/.test(form.nip) || !/^\d{10}$/.test(normalizeNip(form.nip)))) return 'NIP musi zawierać 10 cyfr. Dozwolone separatory: spacja i myślnik.';
  if (form.regon.trim() && !/^(\d{9}|\d{14})$/.test(form.regon.trim())) return 'REGON musi zawierać 9 lub 14 cyfr.';
  if (form.krsNumber.trim() && !/^\d{10}$/.test(form.krsNumber.trim())) return 'KRS musi zawierać 10 cyfr.';

  const address = form.addresses[0];
  if (!address) return 'Podaj adres siedziby.';
  if (!/^[A-Za-z]{2}$/.test(address.countryCode.trim())) return 'Kod kraju musi zawierać 2 litery.';
  if (!address.city.trim()) return 'Podaj miejscowość.';
  if (!address.buildingNumber.trim()) return 'Podaj numer budynku.';

  return null;
}

export function toContractorRequest(form: ContractorFormData): ContractorRequest {
  const nullable = (value: string): string | null => value.trim() || null;

  return {
    fullName: form.fullName.trim(),
    shortName: form.shortName.trim(),
    nip: form.nip === '' ? null : form.nip,
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