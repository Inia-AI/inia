import { BaseService } from '../base'
import type {
  ReRunInputs,
  StartWorkflowExecutionRequest,
  WorkflowExecutionLogSummary,
  WorkflowExecutionLogDetail,
} from './schema'
import type { ServiceParams } from '../schema'
import { WorkflowState } from '@/enums'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async createWorkflow(requestParams: StartWorkflowExecutionRequest) {
    const { data } =
      (await this.instance.post(
        this.getApiPath('workflow-executions'),
        requestParams,
      )) || {}
    return data
  }

  async getReRunInputs(id: string): Promise<ReRunInputs> {
    const { data } = await this.instance.get(
      this.getApiPath(`workflow-executions/${id}/rerun-inputs`),
    )
    return data
  }

  /**
   * Stop an in-flight execution by transitioning it to `Cancelled`.
   * Backend contract: PATCH /workflow-executions/{id}/state — 204 on success,
   * 400 when the current state isn't cancellable, 404 when the id is unknown.
   */
  async cancelWorkflowExecution(id: string) {
    await this.instance.patch(
      this.getApiPath(`workflow-executions/${id}/state`),
      { state: WorkflowState.Cancelled },
    )
  }

  /**
   * Get lightweight summary list of logs for fast initial load
   */
  async getWorkflowExecutionLogs(id: string): Promise<WorkflowExecutionLogSummary[]> {
    const { data } = await this.instance.get(
      this.getApiPath(`workflow-executions/${id}/logs`),
    )
    return data?.value ?? []
  }

  /**
   * Get details for a single log (prompt, output, inputs, placeholders)
   * Call this when user expands a log item.
   * Use includePrompt/includeOutput to only load what's needed for better performance.
   */
  async getLogDetail(
    executionId: string,
    logId: string,
    options?: { includePrompt?: boolean; includeOutput?: boolean },
  ): Promise<WorkflowExecutionLogDetail> {
    const params: Record<string, boolean> = {}
    if (options?.includePrompt !== undefined) {
      params.includePrompt = options.includePrompt
    }
    if (options?.includeOutput !== undefined) {
      params.includeOutput = options.includeOutput
    }

    const { data } = await this.instance.get<WorkflowExecutionLogDetail>(
      this.getApiPath(`workflow-executions/${executionId}/logs/${logId}`),
      { params },
    )
    return data
  }

  async getLogPlaceholders(
    id: string,
    logId: string,
    placeholderKeys?: string[],
  ) {
    const params: Record<string, any> = {}
    if (placeholderKeys && placeholderKeys.length > 0) {
      params.placeholderKeys = placeholderKeys.join(',')
    }

    const { data } = await this.instance.get<Record<string, string>>(
      this.getApiPath(`workflow-executions/${id}/logs/${logId}/placeholders`),
      { params },
    )
    return data || {}
  }
}
