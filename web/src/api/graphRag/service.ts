import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type { GraphQueryRequest, GraphQueryResponse, ValidateTargetResponse } from './schema'

export default class GraphRagService extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Queries the provenance graph to answer questions about how data was generated
   * @param runId - Workflow execution ID
   * @param request - Query request containing targetTag and question
   * @returns AI-generated answer grounded in provenance data
   */
  async queryProvenance(
    runId: string,
    request: GraphQueryRequest
  ): Promise<GraphQueryResponse> {
    const { data } = await this.instance.post<GraphQueryResponse>(
      this.getApiPath(`workflow-executions/${runId}/graph/query`),
      request
    )
    return data
  }

  /**
   * Validates that a target tag exists in the workflow execution graph
   * @param runId - Workflow execution ID
   * @param targetTag - ReplacementTag to validate
   * @returns Validation result
   */
  async validateTarget(
    runId: string,
    targetTag: string
  ): Promise<ValidateTargetResponse> {
    const { data } = await this.instance.get<ValidateTargetResponse>(
      this.getApiPath(`workflow-executions/${runId}/graph/validate/${targetTag}`)
    )
    return data
  }
}
