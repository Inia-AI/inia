import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import router, { NO_ORGANIZATION, NOT_INCLUDED, UNKNOWN_ADDRESS } from './index'
import { queryClient } from '../queryClient'
import API from '@/api'
import { CAPABILITIES } from '@/constants/capabilities'
import { answerService } from '@/test/helpers/answerService'
import { ACTIVE_ORGANIZATION, answerCapabilities, resetCapabilities, without } from '@/test/helpers/capabilities'
import { clearAuthToken, setAuthToken } from '@/test/helpers/jwt'
import { ADMIN_AND_OPERATOR_ROLES, EDITOR_ROLES, OPERATOR_ROLES } from '@/constants/roles'

describe('router', () => {
  beforeEach(async () => {
    await router.push('/login')
  })

  afterEach(() => clearAuthToken())

  it('lets a signed-out visitor reach only the sign-in and password-reset pages and the health check', () => {
    const publicPaths = router
      .getRoutes()
      .filter(route => route.meta.isPublic)
      .map(route => route.path)
      .sort()

    expect(publicPaths).toEqual(['/2fa-verify', '/forgot-password', '/health', '/login', '/reset-password'])
  })

  it('shows the not-found page at an address no page serves, so a bookmark to a removed page does not open an empty screen', () => {
    const unserved = ['/no-such-page', '/retired-area/reports/r1']

    for (const path of unserved) {
      const resolved = router.resolve(path)
      expect(resolved.name).toBe(UNKNOWN_ADDRESS)
      expect(resolved.matched.map(route => route.redirect)).toEqual([undefined])
      expect(resolved.fullPath).toBe(path)
    }
  })

  it('leaves the retired regression and Prompt Library addresses gone', () => {
    const gone = [
      '/workflow-regression',
      '/workflow-regression/batches/b1',
      '/workflow-regression/r1',
      '/regression-baselines',
      '/prompt-library',
    ]

    for (const path of gone) {
      expect(router.resolve(path).name).toBe(UNKNOWN_ADDRESS)
    }
  })

  it('gates the authoring and operations pages on the roles their menu items show to', () => {
    const gated: [string, readonly string[]][] = [
      ['/document-category-management', EDITOR_ROLES],
      ['/counterparty-types', EDITOR_ROLES],
      ['/token-usage', ADMIN_AND_OPERATOR_ROLES],
      ['/model-pricing', OPERATOR_ROLES],
    ]

    for (const [path, roles] of gated) {
      expect(router.resolve(path).meta.allowedRoles).toEqual(roles)
    }
  })

  it('sends a signed-in user whose account names no organization to the page that says so, since every other page would only fail', async () => {
    setAuthToken({ role: 'User' })

    await router.push('/documents')

    expect(router.currentRoute.value.name).toBe(NO_ORGANIZATION)
    expect(router.currentRoute.value.meta.layout).toBe('blank')
  })

  it('takes a user who belongs to an organization away from that page', async () => {
    setAuthToken({ role: 'User', organization_id: 'IniaAI' })

    await router.push('/no-organization')

    expect(router.currentRoute.value.path).toBe('/')
  })
})

describe('an address the organization does not include', () => {
  beforeEach(async () => {
    await router.push('/login')
    setAuthToken({ role: 'User', organization_id: ACTIVE_ORGANIZATION })
  })

  afterEach(() => {
    clearAuthToken()
    resetCapabilities()
    queryClient.clear()
  })

  it('explains that the account does not include it and keeps the address, rather than opening a page that would only fail', async () => {
    answerCapabilities(without(CAPABILITIES.RUN_LOGS))

    await router.push('/workflow-reports/r1/logs?tab=all')

    expect(router.currentRoute.value.name).toBe(NOT_INCLUDED)
    expect(router.currentRoute.value.fullPath).toBe('/workflow-reports/r1/logs?tab=all')
  })

  it('opens the page as before where no organization can lack a capability, and reads no list', async () => {
    const sent = answerService(API.Users.service.instance, () => ({ status: 200, data: { value: [] } }))

    await router.push('/workflows/create')

    expect(router.currentRoute.value.name).toBe('CreateWorkflow')
    expect(sent).toEqual([])
  })

  it('gates each page on the capability the API checks behind it', () => {
    const gated: [string, string][] = [
      ['/workflows/create', CAPABILITIES.RUN],
      ['/workflow-reports/r1/logs', CAPABILITIES.RUN_LOGS],
      ['/signatures', CAPABILITIES.SIGNATURES],
      ['/signatures/t1', CAPABILITIES.SIGNATURES],
    ]

    for (const [path, capability] of gated) {
      expect(router.resolve(path).meta.capability).toBe(capability)
    }
  })
})
