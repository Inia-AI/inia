import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type { AgentActivityDigest } from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  // Without a round the endpoint answers the block's last-collected run, which for a redone
  // member is a later round than the leg being read.
  async getBlockActivity(blockId: string, round?: number) {
    const { data } = await this.instance.get<AgentActivityDigest>(
      this.getApiPath(`workflow-executions/blocks/${blockId}/agent-activity`),
      round === undefined ? undefined : { params: { round } },
    )
    return data
  }
}
