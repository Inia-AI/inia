import { BaseService } from '../base'
import { toOdata } from '../base/adapter'
import type {
  OData,
  ODataQueryParams,
  ODataResponse,
  ServiceParams,
} from '../schema'
import type {
  TokenUsage,
  MyUsageSummary,
  WorkflowTypeUsageSummary,
  OrganizationUsageDashboard,
  ExecutionCost,
} from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getMyUsageSummary(): Promise<MyUsageSummary | null> {
    try {
      const { data } = await this.instance.get<MyUsageSummary>(
        this.getApiPath('token-usage/me'),
      )
      return data
    } catch {
      return null
    }
  }

  async getWorkflowTypeUsageSummary(
    workflowTypeId: string,
  ): Promise<WorkflowTypeUsageSummary | null> {
    try {
      const { data } = await this.instance.get<WorkflowTypeUsageSummary>(
        this.getApiPath(`token-usage/workflow-type/${workflowTypeId}`),
      )
      return data
    } catch {
      return null
    }
  }

  async getOrganizationDashboard(): Promise<OrganizationUsageDashboard | null> {
    try {
      const { data } = await this.instance.get<OrganizationUsageDashboard>(
        this.getApiPath('token-usage/dashboard'),
      )
      return data
    } catch {
      return null
    }
  }

  async listTokenUsage(
    params: ODataQueryParams,
    workflowTypeId?: string,
  ): Promise<OData<TokenUsage> | null> {
    try {
      const queryParams: Record<string, unknown> = { ...params }
      if (workflowTypeId) {
        queryParams.workflowTypeId = workflowTypeId
      }

      const { data } = await this.instance.get<ODataResponse<TokenUsage>>(
        this.getApiPath('token-usage'),
        {
          params: queryParams,
        },
      )
      return toOdata(data)
    } catch {
      return null
    }
  }

  async getExecutionCost(executionId: string): Promise<ExecutionCost> {
    const { data } = await this.instance.get<ExecutionCost>(
      this.getApiPath(`workflow-reports/${executionId}/cost`),
    )
    return data
  }
}
