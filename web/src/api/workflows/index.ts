import { useQuery } from '@tanstack/vue-query'
import type { Ref, MaybeRef } from 'vue'
import { computed, unref } from 'vue'
import API from '@/api'
import Service, { toWorkflowTypeOptions } from './service'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  /**
   * Get workflow type options for dropdown filters.
   * Uses the lightweight summaries endpoint.
   */
  useGetWorkflowTypeOptions = () => {
    return useQuery({
      // Cached under the same key and fetched by the same service as
      // WorkflowCategories.useGetWorkflowCategorySummaries, so the pages that read both share one request.
      queryKey: ['workflow-category-summaries'],
      queryFn: () => API.WorkflowCategories.service.getWorkflowCategorySummaries(),
      select: toWorkflowTypeOptions,
    })
  }

  /**
   * Get workflow summary by workflow type ID.
   * Returns workflow with Variables, DocumentClasses, etc.
   * Called on-demand when user selects a workflow type.
   */
  useGetWorkflowSummaryByType = (
    workflowTypeId: MaybeRef<string | null | undefined>,
    options?: { enabled?: Ref<boolean> | boolean },
  ) => {
    return useQuery({
      queryKey: computed(() => ['workflow-summary', unref(workflowTypeId)]),
      queryFn: async () => {
        const id = unref(workflowTypeId)
        if (!id) return null
        return this.service.getWorkflowSummaryByType(id)
      },
      enabled: options?.enabled,
    })
  }
}
