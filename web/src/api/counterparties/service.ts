import { BaseService } from '../base'
import { parseFilenameFromContentDisposition } from '@/lib/downloadFile'
import type {
  Counterparty,
  CounterpartyBundle,
  CounterpartyDetails,
  CounterpartyImportPlan,
  CreateCounterpartyRequest,
  UpdateCounterpartyRequest,
} from './schema'
import type {
  ServiceParams,
  ODataResponse,
  ODataQueryParams,
  OData,
} from '../schema'
import { toOdata } from '../base/adapter'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getCounterparties(
    params: ODataQueryParams,
  ): Promise<OData<Counterparty> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<Counterparty>>(
        this.getApiPath('counterparties'),
        {
          params,
        },
      )

      return toOdata(data)
    } catch {
      return null
    }
  }

  async getCounterparty(id: string) {
    const { data } = await this.instance.get<CounterpartyDetails>(
      this.getApiPath(`counterparties/${id}`),
    )
    return data
  }

  async createCounterparty(params: CreateCounterpartyRequest) {
    const { data } =
      (await this.instance.post(this.getApiPath('counterparties'), params)) ||
      {}
    return data
  }

  async updateCounterparty(id: string, params: UpdateCounterpartyRequest) {
    const { data } =
      (await this.instance.put(
        this.getApiPath(`counterparties/${id}`),
        params,
      )) || {}
    return data
  }

  async deleteCounterparty(id: string) {
    const { data } =
      (await this.instance.delete(this.getApiPath(`counterparties/${id}`))) ||
      {}
    return data
  }

  async exportBundle(): Promise<{ blob: Blob; filename: string | null }> {
    const response = await this.instance.get<Blob>(
      this.getApiPath('counterparties/export'),
      { responseType: 'blob' },
    )
    return {
      blob: response.data,
      filename: parseFilenameFromContentDisposition(
        response.headers['content-disposition'],
      ),
    }
  }

  async importBundle(
    bundle: CounterpartyBundle,
    dryRun: boolean,
  ): Promise<CounterpartyImportPlan> {
    const { data } = await this.instance.post<CounterpartyImportPlan>(
      this.getApiPath('counterparties/import'),
      bundle,
      { params: { dryRun } },
    )
    return data
  }
}
