
import type { ApiClient } from '../../platform/api/ApiClient';
import { readContractors, readGusContractors } from './ContractorApiContracts';
import type { ContractorDto, ContractorRequest, GusContractorDto, GusIdentifierType } from './ContractorApiContracts';

export class ContractorApiClient {
  constructor(private readonly api: ApiClient) {}

  async getFromGus(identifierType: GusIdentifierType, identifier: string, signal?: AbortSignal): Promise<readonly GusContractorDto[]> {
    if (!identifier.trim()) throw new Error('Podaj NIP, REGON lub KRS.');

    const params = new URLSearchParams({ [identifierType]: identifier.trim() });
    const path = `/api/contractors/gus?${params}`;

    signal?.throwIfAborted();
    const context = this.api.context.capture('tenant');

    const response = await this.api.request<unknown>(path, { scope: 'tenant', signal });

    signal?.throwIfAborted();
    this.api.context.assertCurrent(context);

    return readGusContractors(response);
  }

  async getContractors(signal?: AbortSignal): Promise<readonly ContractorDto[]> {
  signal?.throwIfAborted();
  const context = this.api.context.capture('tenant');
  const response = await this.api.request<unknown>('/api/contractors', { scope: 'tenant', signal });
  signal?.throwIfAborted();
  this.api.context.assertCurrent(context);
  return readContractors(response);
}

async createContractor(request: ContractorRequest, signal?: AbortSignal): Promise<void> {
  signal?.throwIfAborted();
  const context = this.api.context.capture('tenant');
  await this.api.request<unknown>('/api/contractors', { method: 'POST', body: request, scope: 'tenant', signal });
  signal?.throwIfAborted();
  this.api.context.assertCurrent(context);
}
}
