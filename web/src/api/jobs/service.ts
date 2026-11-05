import { BaseService } from '../base'
import { toOdata } from '../base/adapter'
import type {
  OData,
  ODataQueryParams,
  ODataResponse,
  ServiceParams,
} from '../schema'
import { Job } from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getJobs(params: ODataQueryParams): Promise<OData<Job> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<Job>>(
        this.getApiPath('jobs'),
        {
          params,
        },
      )

      return toOdata(data)
    } catch {
      return null
    }
  }
}
