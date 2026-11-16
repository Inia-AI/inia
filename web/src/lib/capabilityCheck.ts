import API from '@/api'
import { TENANTS_QUERY_KEY } from '@/api/users'
import { CAPABILITIES_STALE_MS, capabilitiesOf, capabilitiesVary, holds } from '@/lib/capabilities'
import { queryClient } from '@/plugins/queryClient'

export async function hasCapability(capability: string): Promise<boolean> {
  if (!capabilitiesVary()) {
    return holds(null, capability)
  }
  const tenants = await queryClient
    .fetchQuery({ queryKey: TENANTS_QUERY_KEY, queryFn: () => API.Users.service.getTenants(), staleTime: CAPABILITIES_STALE_MS })
    .catch(() => null)
  return holds(capabilitiesOf(tenants), capability)
}
