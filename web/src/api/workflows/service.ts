import { BaseService } from '../base'
import type { Workflow, WorkflowTypeOption } from './schema'
import type { WorkflowCategorySummary } from '../workflowCategories/schema'
import type { ServiceParams } from '../schema'

export function toWorkflowTypeOptions(categories: WorkflowCategorySummary[]): WorkflowTypeOption[] {
  const options: WorkflowTypeOption[] = []
  const seenTypeIds = new Set<string>()

  for (const category of categories) {
    for (const workflow of category.workflows ?? []) {
      if (workflow.isAvailable && !seenTypeIds.has(workflow.workflowTypeId)) {
        seenTypeIds.add(workflow.workflowTypeId)
        options.push({
          workflowTypeId: workflow.workflowTypeId,
          workflowTypeName: workflow.workflowTypeName ?? workflow.workflowTypeId,
        })
      }
    }
  }

  return options
}

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Get workflow summary by workflow type ID.
   * Returns workflow with Variables, DocumentClasses, etc.
   * Called on-demand when user selects a workflow type.
   */
  async getWorkflowSummaryByType(workflowTypeId: string) {
    const { data } = await this.instance.get<Workflow>(
      this.getApiPath(`workflow-templates/${workflowTypeId}/summary`),
    )

    return data ?? null
  }
}
