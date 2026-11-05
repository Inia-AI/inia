import {
  useMutation,
  UseMutationReturnType,
  useQuery,
  useQueryClient,
} from '@tanstack/vue-query'
import { computed, type Ref } from 'vue'
import Service from './service'
import type {
  StartWorkflowExecutionRequest,
} from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useCreateWorkflowExecution(): UseMutationReturnType<
    any,
    Error,
    any,
    unknown
  > {
    return useMutation({
      mutationFn: (requestParams: StartWorkflowExecutionRequest) =>
        this.service.createWorkflow(requestParams),
    })
  }

  useCancelWorkflowExecution() {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (id: string) => this.service.cancelWorkflowExecution(id),
      onSuccess: () => {
        // Cancel only changes a workflow's state, so refresh the report
        // queries that show that state — don't nuke unrelated caches.
        queryClient.invalidateQueries({ queryKey: ['workflow-reports'] })
      },
    })
  }

  useGetWorkflowExecutionLogs(id: string, enabled: Ref<boolean> = computed(() => true)) {
    return useQuery({
      queryKey: ['workflow-execution-logs', id],
      queryFn: async () => {
        return this.service.getWorkflowExecutionLogs(id)
      },
      enabled,
    })
  }

  getLogPlaceholders(id: string, logId: string, placeholderKeys?: string[]) {
    return this.service.getLogPlaceholders(id, logId, placeholderKeys)
  }

  /**
   * Get details for a single log (prompt, output, inputs, placeholders)
   * Call this when user expands a log item.
   * Use includePrompt/includeOutput to only load what's needed.
   */
  getLogDetail(
    executionId: string,
    logId: string,
    options?: { includePrompt?: boolean; includeOutput?: boolean },
  ) {
    return this.service.getLogDetail(executionId, logId, options)
  }
}
