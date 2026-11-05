import { BaseService } from '../base'
import type {
  ModelPricing,
  ModelPricingListItem,
  CreateModelPricingRequest,
  UpdateModelPricingRequest,
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

  async getModelPricings(
    params: ODataQueryParams,
  ): Promise<OData<ModelPricingListItem> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<ModelPricingListItem>>(
        this.getApiPath('model-pricing'),
        {
          params,
        },
      )

      return toOdata(data)
    } catch {
      return null
    }
  }

  async getModelPricing(id: string): Promise<ModelPricing> {
    const { data } = await this.instance.get<ModelPricing>(
      this.getApiPath(`model-pricing/${id}`),
    )
    return data
  }

  async createModelPricing(params: CreateModelPricingRequest): Promise<ModelPricing> {
    const { data } = await this.instance.post<ModelPricing>(
      this.getApiPath('model-pricing'),
      params,
    )
    return data
  }

  async updateModelPricing(
    id: string,
    params: UpdateModelPricingRequest,
  ): Promise<void> {
    await this.instance.put(
      this.getApiPath(`model-pricing/${id}`),
      params,
    )
  }

  async deleteModelPricing(id: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`model-pricing/${id}`))
  }
}
