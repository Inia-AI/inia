import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { GraphQueryRequest, GraphQueryResponse, ValidateTargetResponse } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  /**
   * Mutation hook for querying provenance
   * @returns Mutation hook that accepts { runId, request }
   */
  useQueryProvenance(): UseMutationReturnType<
    GraphQueryResponse,
    Error,
    { runId: string; request: GraphQueryRequest },
    unknown
  > {
    return useMutation({
      mutationFn: ({ runId, request }: { runId: string; request: GraphQueryRequest }) =>
        this.service.queryProvenance(runId, request),
    })
  }

  /**
   * Mutation hook for validating target tag
   * @returns Mutation hook that accepts { runId, targetTag }
   */
  useValidateTarget(): UseMutationReturnType<
    ValidateTargetResponse,
    Error,
    { runId: string; targetTag: string },
    unknown
  > {
    return useMutation({
      mutationFn: ({ runId, targetTag }: { runId: string; targetTag: string }) =>
        this.service.validateTarget(runId, targetTag),
    })
  }
}
