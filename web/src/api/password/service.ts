import { BaseService } from '../base'
import type { ForgotPasswordRequest, ResetPasswordRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async forgotPassword(params: ForgotPasswordRequest) {
    await this.instance.post(this.getApiPath(`passwords/forgot`), params)
  }

  async resetPassword(params: ResetPasswordRequest) {
    await this.instance.post(this.getApiPath(`passwords/reset`), params)
  }
}
