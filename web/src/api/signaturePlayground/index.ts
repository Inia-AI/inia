import { computed, unref, type MaybeRef } from 'vue'
import { useMutation, useQuery } from '@tanstack/vue-query'
import Service from './service'
import type { ServiceParams } from '../schema'
import type {
  ExecutionBlocks,
  SignaturePromptTestResult,
  TestSignaturePromptRequest,
} from './types'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetExecutionBlocks = (workflowExecutionId: MaybeRef<string>) => {
    const queryKey = computed(() => [
      'signature-playground',
      'execution-blocks',
      unref(workflowExecutionId),
    ])
    return useQuery<ExecutionBlocks>({
      queryKey,
      queryFn: () => this.service.getExecutionBlocks(unref(workflowExecutionId)),
      enabled: computed(() => unref(workflowExecutionId).length > 0),
    })
  }

  /**
   * Fires a one-shot test run. Not invalidating any query on success because
   * nothing persists — each replay is independent.
   */
  useTestSignaturePromptMutation = (workflowExecutionId: MaybeRef<string>) => {
    return useMutation<SignaturePromptTestResult, unknown, TestSignaturePromptRequest>({
      mutationFn: (body) =>
        this.service.testSignaturePrompt(unref(workflowExecutionId), body),
    })
  }
}
