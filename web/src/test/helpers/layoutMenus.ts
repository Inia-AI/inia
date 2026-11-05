import { computed, defineComponent, h, ref, type Component } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { expect, vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import type { EN } from '@/assets/locales/en'

/** The navigation call that the mocked `vue-router` below hands to the layout. */
export const pushMock = vi.fn()

export const vueRouterModule = {
  useRouter: () => ({ push: pushMock, currentRoute: ref({ path: '/counterparty-management' }) }),
  RouterLink: defineComponent({
    name: 'RouterLinkStub',
    props: { to: { type: [String, Object], required: true } },
    setup(props, { slots }) {
      return () => h('a', { href: String(props.to) }, slots.default?.())
    },
  }),
}

export const apiModule = {
  default: {
    Meta: { useGetVersion: () => ({ data: ref(undefined) }) },
    Diagnostics: { useDownloadLog: () => ({ mutateAsync: vi.fn() }) },
    Users: { useGetTenants: () => ({ data: ref(undefined) }) },
  },
}

export const reviewCountsModule = {
  useMyReviewCounts: () => ({
    totalCount: computed(() => 0),
    proposerTotalCount: computed(() => 0),
  }),
}

/** `@/lib/utils` for a signed-in SuperAdmin with one tenant. */
export const superAdminUtilsModule = <T extends object>(actual: T) => ({
  ...actual,
  getDecodedToken: () => undefined,
  hasRole: (role: string) => role === 'SuperAdmin',
  hasAnyRole: (roles: readonly string[]) => roles.includes('SuperAdmin'),
  hasMultipleTenants: () => false,
})

export const settle = async () => {
  await flushPromises()
  await new Promise(resolve => setTimeout(resolve, 10))
  await flushPromises()
}

export const press = async (key: string) => {
  ;(document.activeElement ?? document.body).dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
  )
  await settle()
}

export const focused = () => document.activeElement as HTMLElement

export const mountLayout = async (layout: Component, messages: typeof EN) => {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: messages }, missingWarn: false, fallbackWarn: false })
  const wrapper = mount(layout, {
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { IniaIcon: true, ThemeToggle: true } },
  })
  await settle()
  return wrapper
}

export const focusTrigger = async (wrapper: Awaited<ReturnType<typeof mountLayout>>, name: string) => {
  const trigger = wrapper
    .findAll('[aria-haspopup="menu"]')
    .find(button => (button.attributes('aria-label') ?? button.text()) === name)
  expect(trigger, `no menu button named ${name}`).toBeDefined()
  const element = trigger!.element as HTMLElement
  element.focus()
  await settle()
  return element
}
