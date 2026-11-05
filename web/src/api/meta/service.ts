import { BaseService } from '../base'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getVersion() {
    try {
      const { data } = await this.instance.get(this.getApiPath(`meta/version`))
      return data
    } catch {
      return null
    }
  }
}
