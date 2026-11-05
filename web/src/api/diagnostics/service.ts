import { BaseService } from '../base'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async downloadLog() {
    const response = await this.instance.get(
      this.getApiPath('diagnostics/logs/download'),
      {
        responseType: 'blob',
      },
    )
    return response?.data
  }
}
