import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { ChangePasswordRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useChangePassword(): UseMutationReturnType<any, Error, any, unknown> {
    return useMutation({
      mutationFn: (params: ChangePasswordRequest) =>
        this.service.changePassword(params),
    })
  }
}
