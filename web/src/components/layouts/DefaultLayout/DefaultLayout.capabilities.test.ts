import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createI18n } from 'vue-i18n'
import API from '@/api'
import { EN } from '@/assets/locales/en'
import { CAPABILITIES } from '@/constants/capabilities'
import { extensionPoints } from '@/modules/extensionPoints'
import { answerService } from '@/test/helpers/answerService'
import { answerCapabilities, EVERY_CAPABILITY, resetCapabilities, without } from '@/test/helpers/capabilities'

vi.mock('vue-router', async () => (await import('@/test/helpers/layoutMenus')).vueRouterModule)
vi.mock('@/composables/useMyReviewCounts', async () => (await import('@/test/helpers/layoutMenus')).reviewCountsModule)
vi.mock('@/lib/utils', async importOriginal =>
  (await import('@/test/helpers/layoutMenus')).superAdminUtilsModule(await importOriginal<typeof import('@/lib/utils')>()),
)

import DefaultLayout from './DefaultLayout.vue'

const SCREEN_GATED = ['Drawer page', 'Group page', 'Signatures']

const mountLayout = async () => {
  answerService(API.Meta.service.instance, () => ({ status: 200, data: { version: '1' } }))
  extensionPoints.drawerMenu.push({ titleKey: 'test.drawerPage', to: '/drawer-page', icon: 'folder', placement: 'first', capability: CAPABILITIES.VALIDATION })
  extensionPoints.topMenu.push({ titleKey: 'test.group', pathPrefix: '/group/', links: [{ titleKey: 'test.groupPage', to: '/group/page', capability: CAPABILITIES.MONITORING }] })
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en: { ...EN, test: { drawerPage: 'Drawer page', group: 'Group', groupPage: 'Group page' } } },
    missingWarn: false,
    fallbackWarn: false,
  })
  const wrapper = mount(DefaultLayout, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]], stubs: { IniaIcon: true, ThemeToggle: true } },
  })
  await flushPromises()
  return wrapper
}

const gatedEntries = () =>
  [...new Set([...document.querySelectorAll('a, button')].map(element => element.textContent?.trim() ?? ''))].filter(text => SCREEN_GATED.includes(text)).sort()

let mounted: Awaited<ReturnType<typeof mountLayout>> | undefined

describe('DefaultLayout and the organization’s capabilities', () => {
  afterEach(() => {
    mounted?.unmount()
    resetCapabilities()
    extensionPoints.drawerMenu.splice(0)
    extensionPoints.topMenu.splice(0)
  })

  it('shows module pages and Signatures to an organization that has their capabilities', async () => {
    answerCapabilities(EVERY_CAPABILITY)
    mounted = await mountLayout()

    expect(gatedEntries()).toEqual([...SCREEN_GATED].sort())
  })

  it('hides each menu entry whose capability the organization lacks', async () => {
    answerCapabilities(without(CAPABILITIES.VALIDATION, CAPABILITIES.MONITORING, CAPABILITIES.SIGNATURES))
    mounted = await mountLayout()

    expect(gatedEntries()).toEqual([])
  })
})
