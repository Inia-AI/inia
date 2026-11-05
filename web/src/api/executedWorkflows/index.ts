import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type {
  DownloadExecutedWorkflowParams,
  DownloadExecutedWorkflowResult,
} from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useDownloadExecutedWorkflow(): UseMutationReturnType<
    DownloadExecutedWorkflowResult,
    Error,
    DownloadExecutedWorkflowParams,
    unknown
  > {
    return useMutation({
      mutationFn: (params: DownloadExecutedWorkflowParams) =>
        this.service.downloadExecutedWorkflow(params),
    })
  }
}
