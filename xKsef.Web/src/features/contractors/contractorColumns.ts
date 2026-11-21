import type { TableColumn } from '../../shared/table/TableColumn';
import type { ContractorDto } from './ContractorApiContracts';

export function contractorColumns(): readonly TableColumn<ContractorDto>[] {
  return [
    { id: 'fullName', header: 'Nazwa', kind: 'text', value: row => row.fullName, width: 280 },
    { id: 'shortName', header: 'Nazwa skrócona', kind: 'text', value: row => row.shortName, width: 180 },
    { id: 'nip', header: 'NIP', kind: 'text', value: row => row.nip, width: 135 },
    { id: 'regon', header: 'REGON', kind: 'text', value: row => row.regon, width: 135 },
    { id: 'krsNumber', header: 'KRS', kind: 'text', value: row => row.krsNumber, width: 135 },
    { id: 'isActive', header: 'Status', kind: 'text', value: row => row.isActive ? 'Aktywny' : 'Nieaktywny', width: 120 },
  ];
}