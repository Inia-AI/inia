import { computed, Ref } from 'vue'
import {
  useQuery,
  UseQueryReturnType,
} from '@tanstack/vue-query'
import Service from './service'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'
import type {
  TokenUsage,
  MyUsageSummary,
  WorkflowTypeUsageSummary,
  OrganizationUsageDashboard,
  ExecutionCost,
} from './schema'

export default class {
  service: Service

  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetMyUsageSummary(): UseQueryReturnType<MyUsageSummary | null, Error> {
    return useQuery({
      queryKey: ['tokenUsage', 'me'],
      queryFn: () => this.service.getMyUsageSummary(),
    })
  }

  useGetWorkflowTypeUsageSummary(
    workflowTypeId: Ref<string>,
  ): UseQueryReturnType<WorkflowTypeUsageSummary | null, Error> {
    return useQuery({
      queryKey: computed(() => ['tokenUsage', 'workflowType', workflowTypeId.value]),
      queryFn: () => this.service.getWorkflowTypeUsageSummary(workflowTypeId.value),
      enabled: computed(() => !!workflowTypeId.value),
    })
  }

  useGetOrganizationDashboard(): UseQueryReturnType<OrganizationUsageDashboard | null, Error> {
    return useQuery({
      queryKey: ['tokenUsage', 'dashboard'],
      queryFn: () => this.service.getOrganizationDashboard(),
    })
  }

  useListTokenUsage(
    params: Ref<ODataQueryParams>,
    workflowTypeId?: Ref<string | undefined>,
  ): UseQueryReturnType<OData<TokenUsage> | null, Error> {
    return useQuery({
      queryKey: computed(() => [
        'tokenUsage',
        'list',
        JSON.stringify(params.value),
        workflowTypeId?.value,
      ]),
      queryFn: () => this.service.listTokenUsage(params.value, workflowTypeId?.value),
    })
  }

  useGetExecutionCost(
    executionId: Ref<string>,
    enabled: Ref<boolean>,
  ): UseQueryReturnType<ExecutionCost, Error> {
    return useQuery({
      queryKey: computed(() => ['tokenUsage', 'executionCost', executionId.value]),
      queryFn: () => this.service.getExecutionCost(executionId.value),
      enabled: computed(() => enabled.value && !!executionId.value),
    })
  }
}

export * from './schema'
