import API from '@/api'
import { TENANTS_QUERY_KEY } from '@/api/users'
import { CAPABILITIES_STALE_MS, capabilitiesOf, capabilitiesVary } from '@/lib/capabilities'
import { queryClient } from '@/plugins/queryClient'

export async function hasCapability(capability: string): Promise<boolean> {
  if (!capabilitiesVary()) {
    return true
  }
  const tenants = await queryClient
    .fetchQuery({ queryKey: TENANTS_QUERY_KEY, queryFn: () => API.Users.service.getTenants(), staleTime: CAPABILITIES_STALE_MS })
    .catch(() => null)
  return capabilitiesOf(tenants)?.has(capability) ?? true
}
