import { ApiError } from '../api/ApiError';

export interface BrowserUser { readonly userId: string; readonly login: string }
export interface Tenant { readonly id: string; readonly name: string; readonly role: 'Admin' | 'User' }
export function readUser(value: unknown): BrowserUser {
  if (!value || typeof value !== 'object' || !('userId' in value) || typeof value.userId !== 'string' || !value.userId
      || !('login' in value) || typeof value.login !== 'string') throw new ApiError(502, 'Nieprawidłowe dane użytkownika z API.');
  return { userId: value.userId, login: value.login };
}
export function readTenants(value: unknown): readonly Tenant[] {
  if (!Array.isArray(value)) throw new ApiError(502, 'Nieprawidłowa lista tenantów z API.');
  const rows = value.map((row: unknown): Tenant => {
    if (!row || typeof row !== 'object' || !('id' in row) || typeof row.id !== 'string' || !row.id || !('name' in row) || typeof row.name !== 'string'
        || !('role' in row) || row.role !== 'Admin' && row.role !== 'User') throw new ApiError(502, 'Nieprawidłowe dane tenanta z API.');
    return { id: row.id, name: row.name, role: row.role };
  });
  if (new Set(rows.map(row => row.id)).size !== rows.length) throw new ApiError(502, 'Niejednoznaczna lista tenantów z API.');
  return rows;
}
