import { BaseService } from '../base'
import type { CounterpartyType, CounterpartyTypeRequest } from './schema'
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

  async getCounterpartyTypes(
    params: ODataQueryParams,
  ): Promise<OData<CounterpartyType> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<CounterpartyType>>(
        this.getApiPath('counterparty-types'),
        {
          params,
        },
      )

      return toOdata(data)
    } catch {
      return null
    }
  }

  async createCounterpartyType(request: CounterpartyTypeRequest): Promise<CounterpartyType> {
    const { data } = await this.instance.post<CounterpartyType>(
      this.getApiPath('counterparty-types'),
      request,
    )
    return data
  }

  async updateCounterpartyType(id: string, request: CounterpartyTypeRequest): Promise<CounterpartyType> {
    const { data } = await this.instance.put<CounterpartyType>(
      this.getApiPath(`counterparty-types/${id}`),
      request,
    )
    return data
  }

  async deleteCounterpartyType(id: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`counterparty-types/${id}`))
  }
}
