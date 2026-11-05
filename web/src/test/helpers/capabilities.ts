import API from '@/api'
import { CAPABILITIES } from '@/constants/capabilities'
import { extensionPoints } from '@/modules/extensionPoints'
import { answerService, type Sent } from './answerService'

export const EVERY_CAPABILITY: readonly string[] = Object.values(CAPABILITIES)

export const ACTIVE_ORGANIZATION = 'org-active'

const varies = () => true

/** Makes the deployment one where capabilities vary, with the active organization's tenant row for the given list. */
export function makeCapabilitiesVary(capabilities: readonly string[]) {
  extensionPoints.capabilitiesVary.push(varies)
  localStorage.setItem('selectedOrgId', ACTIVE_ORGANIZATION)
  return [{ id: ACTIVE_ORGANIZATION, name: 'Acme', capabilities: [...capabilities] }]
}

/** As makeCapabilitiesVary, and answers the tenants read at the HTTP boundary. */
export function answerCapabilities(capabilities: readonly string[] | null): Sent[] {
  extensionPoints.capabilitiesVary.push(varies)
  localStorage.setItem('selectedOrgId', ACTIVE_ORGANIZATION)
  return answerService(API.Users.service.instance, () => ({
    status: 200,
    data: { value: capabilities === null ? null : [{ id: ACTIVE_ORGANIZATION, name: 'Acme', capabilities }, { id: 'org-other', name: 'Other', capabilities: EVERY_CAPABILITY }] },
  }))
}

export const without = (...missing: string[]) => EVERY_CAPABILITY.filter(capability => !missing.includes(capability))

export function resetCapabilities() {
  extensionPoints.capabilitiesVary.splice(0)
  localStorage.removeItem('selectedOrgId')
}
