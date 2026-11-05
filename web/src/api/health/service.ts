import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type { HealthResponse } from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getHealth(): Promise<HealthResponse> {
    try {
      const { data } = await this.instance.get<HealthResponse>('health')
      return data
    } catch (error) {
      throw new Error(
        'Health check failed: ' +
          (error instanceof Error ? error.message : 'Unknown error'),
      )
    }
  }
}
