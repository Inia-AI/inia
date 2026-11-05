import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'

import Service from './service'
import type { RefineContentRequest, RefineContentResponse } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useRefineContent(): UseMutationReturnType<RefineContentResponse, Error, RefineContentRequest, unknown> {
    return useMutation({
      mutationFn: (params: RefineContentRequest) => this.service.refineContent(params),
    })
  }
}
