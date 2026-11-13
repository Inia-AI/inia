import { BaseService } from '../base'
import type { ChangePasswordRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async changePassword(params: ChangePasswordRequest) {
    await this.instance.post(
      this.getApiPath(`profiles/change-password`),
      params,
    )
  }

  async signOutEverywhere() {
    await this.instance.post(this.getApiPath(`profiles/sign-out-everywhere`))
  }
}
