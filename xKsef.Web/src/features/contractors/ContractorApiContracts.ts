
import { ApiError } from '../../platform/api/ApiError';

export interface GusAddressDto {
  readonly voivodeship: string | null;
  readonly county: string | null;
  readonly municipality: string | null;
  readonly city: string | null;
  readonly postalCode: string | null;
  readonly street: string | null;
  readonly buildingNumber: string | null;
  readonly apartmentNumber: string | null;
  readonly postOfficeCity: string | null;
}

export interface GusContractorDto {
  readonly nip: string | null;
  readonly regon: string;
  readonly krs: string | null;
  readonly fullName: string;
  readonly shortName: string | null;
  readonly address: GusAddressDto;
}

export interface ContractorAddressDto {
  addressType: number;
  countryCode: string;
  street: string;
  buildingNumber: string;
  apartmentNumber: string | null;
  postalCode: string;
  city: string;
}

export interface ContractorDto {
  id: string;
  isActive: boolean;
  createdAt: string;
  fullName: string;
  shortName: string;
  nip: string | null;
  regon: string | null;
  krsNumber: string | null;
  euVatNumber: string | null;
  otherTaxNumber: string | null;
  isEuVatRegistered: boolean;
  addresses: ContractorAddressDto[];
}

export type GusIdentifierType = 'nip' | 'regon' | 'krs';

function invalid(message = 'Nieprawidłowa odpowiedź API GUS.'): never {
  throw new ApiError(502, message);
}

function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid();
  return value as Record<string, unknown>;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : invalid();
}

function nullableText(value: unknown): string | null {
  return value === null ? null : text(value);
}

function readAddress(value: unknown): GusAddressDto {
  const row = record(value);

  return {
    voivodeship: nullableText(row.voivodeship),
    county: nullableText(row.county),
    municipality: nullableText(row.municipality),
    city: nullableText(row.city),
    postalCode: nullableText(row.postalCode),
    street: nullableText(row.street),
    buildingNumber: nullableText(row.buildingNumber),
    apartmentNumber: nullableText(row.apartmentNumber),
    postOfficeCity: nullableText(row.postOfficeCity),
  };
}

function readGusContractor(value: unknown): GusContractorDto {
  const row = record(value);
  const regon = text(row.regon);
  const fullName = text(row.fullName);

  if (!regon.trim() || !fullName.trim()) return invalid();

  return {
    nip: nullableText(row.nip),
    regon,
    krs: nullableText(row.krs),
    fullName,
    shortName: nullableText(row.shortName),
    address: readAddress(row.address),
  };
}

export function readGusContractors(value: unknown): readonly GusContractorDto[] {
  if (!Array.isArray(value)) return invalid();
  return value.map(readGusContractor);
}

const invalidContractors = (): never => invalid('Nieprawidłowa odpowiedź API kontrahentów.');

function contractorRecord(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalidContractors();
  return value as Record<string, unknown>;
}

function contractorText(value: unknown): string {
  return typeof value === 'string' ? value : invalidContractors();
}

function contractorNullableText(value: unknown): string | null {
  return value === null ? null : contractorText(value);
}

function contractorBoolean(value: unknown): boolean {
  return typeof value === 'boolean' ? value : invalidContractors();
}

function contractorNumber(value: unknown): number {
  return typeof value === 'number' && Number.isInteger(value) ? value : invalidContractors();
}

function readContractorAddress(value: unknown): ContractorAddressDto {
  const row = contractorRecord(value);

  return {
    addressType: contractorNumber(row.addressType),
    countryCode: contractorText(row.countryCode),
    street: contractorText(row.street),
    buildingNumber: contractorText(row.buildingNumber),
    apartmentNumber: contractorNullableText(row.apartmentNumber),
    postalCode: contractorText(row.postalCode),
    city: contractorText(row.city),
  };
}

export function readContractor(value: unknown): ContractorDto {
  const row = contractorRecord(value);

  if (!Array.isArray(row.addresses)) return invalidContractors();

  const id = contractorText(row.id);
  const createdAt = contractorText(row.createdAt);
  const fullName = contractorText(row.fullName);

  if (!id.trim() || !createdAt.trim() || !fullName.trim()) return invalidContractors();

  return {
    id,
    isActive: contractorBoolean(row.isActive),
    createdAt,
    fullName,
    shortName: contractorText(row.shortName),
    nip: contractorNullableText(row.nip),
    regon: contractorNullableText(row.regon),
    krsNumber: contractorNullableText(row.krsNumber),
    euVatNumber: contractorNullableText(row.euVatNumber),
    otherTaxNumber: contractorNullableText(row.otherTaxNumber),
    isEuVatRegistered: contractorBoolean(row.isEuVatRegistered),
    addresses: row.addresses.map(readContractorAddress),
  };
}

export function readContractors(value: unknown): readonly ContractorDto[] {
  if (!Array.isArray(value)) return invalidContractors();
  return value.map(readContractor);
}

export interface ContractorRequest {
  fullName: string;
  shortName: string;
  nip: string | null;
  regon: string | null;
  krsNumber: string | null;
  euVatNumber: string | null;
  otherTaxNumber: string | null;
  isEuVatRegistered: boolean;
  isActive: boolean | null;
  addresses: ContractorAddressDto[] | null;
}
