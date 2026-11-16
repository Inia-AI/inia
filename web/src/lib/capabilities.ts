import type { UserTenant } from '@/api/users/schema'
import { CAPABILITIES } from '@/constants/capabilities'
import { activeOrganizationId } from '@/lib/utils'
import { extensionPoints } from '@/modules/extensionPoints'

export const CAPABILITIES_STALE_MS = 60_000

export const capabilitiesVary = () => extensionPoints.capabilitiesVary.some(varies => varies())

// Null when the read gives no list, which leaves every feature on: the API refuses what is missing either way.
export function capabilitiesOf(tenants: UserTenant[] | null | undefined): ReadonlySet<string> | null {
  const active = activeOrganizationId()
  const capabilities = tenants?.find(tenant => tenant.id === active)?.capabilities
  return capabilities ? new Set(capabilities) : null
}

const GRANTED_BY_DEFAULT: ReadonlySet<string> = new Set(Object.values(CAPABILITIES))

// Without a list, an organization holds what the platform grants to everyone, and nothing a module adds.
export function holds(capabilities: ReadonlySet<string> | null, capability: string): boolean {
  return capabilities?.has(capability) ?? GRANTED_BY_DEFAULT.has(capability)
}
