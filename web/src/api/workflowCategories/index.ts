import { useQuery } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import Service from './service'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  /**
   * Get workflow category summaries (lightweight).
   * Returns categories with workflow names but not full details.
   * Used for initial page load.
   */
  useGetWorkflowCategorySummaries = (options: { enabled?: MaybeRefOrGetter<boolean>; staleTime?: number; refetchOnWindowFocus?: boolean } = {}) => {
    return useQuery({
      queryKey: ['workflow-category-summaries'],
      queryFn: async () => {
        return this.service.getWorkflowCategorySummaries()
      },
      ...options,
    })
  }
}
