import { BaseService } from '../base'
import type { User, UserTenant, ResolvedUser, ResolveUsersRequest } from './schema'
import type {
  ServiceParams,
  ODataResponse,
  ODataQueryParams,
  OData,
  ListResponse,
} from '../schema'
import { toOdata } from '../base/adapter'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getUsers(params: ODataQueryParams): Promise<OData<User> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<User>>(
        this.getApiPath('users'),
        {
          params,
        },
      )

      return toOdata(data)
    } catch {
      return null
    }
  }

  async getTenants(): Promise<UserTenant[] | null> {
    try {
      const { data } = await this.instance.get<ListResponse<UserTenant>>(
        this.getApiPath('users/me/tenants'),
      )

      if (!data) {
        return data
      }

      return data?.value
    } catch {
      return null
    }
  }

  async resolveUsers(usernames: string[]): Promise<ResolvedUser[] | null> {
    try {
      const { data } = await this.instance.post<ListResponse<ResolvedUser>>(
        this.getApiPath('users/resolve'),
        { usernames } as ResolveUsersRequest,
      )

      return data?.value ?? null
    } catch {
      return null
    }
  }
}
