import { BaseService } from '../base'
import type { WorkflowCategorySummary } from './schema'
import type { ListResponse, ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Get workflow category summaries (lightweight).
   * Returns categories with workflow names but not full details.
   * Used for initial page load.
   */
  async getWorkflowCategorySummaries() {
    const { data } = await this.instance.get<ListResponse<WorkflowCategorySummary>>(
      this.getApiPath('workflow-categories/summaries'),
    )

    return data?.value ?? []
  }
}
