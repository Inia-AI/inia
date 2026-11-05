import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { ForgotPasswordRequest, ResetPasswordRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useForgotPassword(): UseMutationReturnType<void, Error, ForgotPasswordRequest, unknown> {
    return useMutation({
      mutationFn: (params: ForgotPasswordRequest) =>
        this.service.forgotPassword(params),
    })
  }

  useResetPassword(): UseMutationReturnType<void, Error, ResetPasswordRequest, unknown> {
    return useMutation({
      mutationFn: (params: ResetPasswordRequest) => this.service.resetPassword(params),
    })
  }
}
