import { computed, Ref } from 'vue'
import { useQuery, UseQueryReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { User, UserTenant, ResolvedUser } from './schema'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'

export const TENANTS_QUERY_KEY = ['users/me/tenants']

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetUsers(
    params: Ref<ODataQueryParams>,
    enabled: Ref<boolean> = computed(() => true),
  ): UseQueryReturnType<OData<User>, Error> {
    return useQuery({
      queryKey: computed(() => ['users', JSON.stringify(params.value)]),
      queryFn: () => this.service.getUsers(params.value),
      enabled,
    })
  }

  useGetTenants(enabled: Ref<boolean> = computed(() => true), staleTime = 0): UseQueryReturnType<UserTenant[] | null, Error> {
    return useQuery({
      queryKey: TENANTS_QUERY_KEY,
      queryFn: () => this.service.getTenants(),
      enabled,
      staleTime,
    })
  }

  useResolveUsers(
    usernames: Ref<string[]>,
  ): UseQueryReturnType<ResolvedUser[], Error> {
    return useQuery({
      queryKey: computed(() => ['users/resolve', JSON.stringify(usernames.value)]),
      queryFn: () => this.service.resolveUsers(usernames.value),
      enabled: computed(() => usernames.value.length > 0),
    })
  }
}
