import { BaseService } from '../base'
import type { RefineContentRequest, RefineContentResponse } from './schema'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async refineContent(params: RefineContentRequest): Promise<RefineContentResponse> {
    const { data } =
      (await this.instance.post(this.getApiPath('ai/content-refinement'), params)) || {}
    return data
  }
}
