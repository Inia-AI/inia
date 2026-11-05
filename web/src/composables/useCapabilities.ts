import { computed, type ComputedRef } from 'vue'
import API from '@/api'
import { CAPABILITIES } from '@/constants/capabilities'
import { ROLES } from '@/constants/roles'
import { hasAnyRole } from '@/lib/utils'
import { CAPABILITIES_STALE_MS, capabilitiesOf, capabilitiesVary } from '@/lib/capabilities'
import { extensionPoints } from '@/modules/extensionPoints'

// Until the list arrives nothing gated shows, so a feature the organization lacks never flashes on screen.
const NONE_YET: ReadonlySet<string> = new Set()

export function useCapabilities(): (capability: string) => boolean {
  if (extensionPoints.capabilitiesVary.length === 0) {
    return () => true
  }
  const varies = computed(capabilitiesVary)
  const { data: tenants, isPending } = API.Users.useGetTenants(varies, CAPABILITIES_STALE_MS)
  const capabilities = computed(() => {
    if (!varies.value) {
      return null
    }
    return isPending.value ? NONE_YET : capabilitiesOf(tenants.value)
  })
  return capability => capabilities.value?.has(capability) ?? true
}

export function useCanAskAi(): ComputedRef<boolean> {
  const has = useCapabilities()
  return computed(() => has(CAPABILITIES.EXPLORE) || has(CAPABILITIES.REFINE))
}

export function useCanSeeTeam(): ComputedRef<boolean> {
  const has = useCapabilities()
  return computed(() => has(CAPABILITIES.TEAM_VIEW))
}

export function useCanUploadDocuments(): ComputedRef<boolean> {
  const has = useCapabilities()
  return computed(() => has(CAPABILITIES.UPLOAD) && has(CAPABILITIES.STANDALONE_PROCESSING))
}

export function useCanReviewSignatures(): ComputedRef<boolean> {
  const has = useCapabilities()
  return computed(() => hasAnyRole([ROLES.SUPER_ADMIN, ROLES.SIGNATURE_CONTRIBUTOR]) && has(CAPABILITIES.SIGNATURES))
}
