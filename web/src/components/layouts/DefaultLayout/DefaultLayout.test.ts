import { afterEach, describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { computed, defineComponent, h, ref } from 'vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), currentRoute: ref({ path: '/' }) }),
  RouterLink: defineComponent({
    name: 'RouterLinkStub',
    setup(_, { slots }) {
      return () => h('a', slots.default?.())
    },
  }),
}))

vi.mock('@/api', () => ({
  default: {
    Meta: { useGetVersion: () => ({ data: ref(undefined) }) },
    Diagnostics: { useDownloadLog: () => ({ mutateAsync: vi.fn() }) },
    Users: { useGetTenants: () => ({ data: ref(undefined) }) },
  },
}))

vi.mock('@/composables/useMyReviewCounts', () => ({
  useMyReviewCounts: () => ({
    totalCount: computed(() => 0),
    proposerTotalCount: computed(() => 0),
  }),
}))

vi.mock('@/lib/utils', async importOriginal => ({
  ...(await importOriginal<typeof import('@/lib/utils')>()),
  getDecodedToken: () => undefined,
  hasRole: (role: string) => role === 'SuperAdmin',
  hasAnyRole: (roles: readonly string[]) => roles.includes('SuperAdmin'),
  hasMultipleTenants: () => false,
}))

vi.mock('@/components/ui/dropdown-menu', () => {
  const passthrough = (name: string) =>
    defineComponent({
      name,
      setup(_, { slots }) {
        return () => h('div', { 'data-stub': name }, slots.default?.())
      },
    })
  return {
    DropdownMenu: passthrough('DropdownMenu'),
    DropdownMenuContent: passthrough('DropdownMenuContent'),
    DropdownMenuItem: passthrough('DropdownMenuItem'),
    DropdownMenuTrigger: defineComponent({
      name: 'DropdownMenuTriggerStub',
      setup(_, { slots }) {
        return () => h('button', { 'data-stub': 'DropdownMenuTrigger' }, slots.default?.())
      },
    }),
  }
})

import DefaultLayout from './DefaultLayout.vue'

const mountLayout = () =>
  mount(DefaultLayout, {
    global: { stubs: { IniaIcon: true, ThemeToggle: true } },
  })

describe('DefaultLayout header', () => {
  it('names the account menu trigger that holds Profile and Logout', () => {
    const wrapper = mountLayout()
    const accountTrigger = wrapper
      .findAll('[data-stub="DropdownMenuTrigger"]')
      .find(trigger => trigger.attributes('aria-label') === 'shared.layout.accountMenu')

    expect(accountTrigger).toBeDefined()
  })

  it('makes the logo a named button that goes to the dashboard', () => {
    const wrapper = mountLayout()
    const logoButtons = wrapper.findAll('button[aria-label="shared.layout.home"]')

    expect(logoButtons.length).toBeGreaterThan(0)
    for (const button of logoButtons) {
      expect(button.find('svg').attributes('aria-hidden')).toBe('true')
    }
  })

  it('offers the log download as a named button', () => {
    const wrapper = mountLayout()

    expect(wrapper.find('button[aria-label="shared.layout.downloadLog"]').exists()).toBe(true)
  })
})

describe('DefaultLayout version line', () => {
  const mountWithVersion = async (version: string) => {
    vi.stubEnv('VITE_VERSION', version)
    vi.resetModules()
    const { default: BuiltLayout } = await import('./DefaultLayout.vue')
    return mount(BuiltLayout, { global: { stubs: { IniaIcon: true, ThemeToggle: true } } }).text()
  }

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('shows no frontend version when the build sets none', async () => {
    const text = await mountWithVersion('')

    expect(text).not.toContain('VITE_VERSION_PLACEHOLDER')
    expect(text).not.toContain('Frontend Version')
  })

  it('shows the version the build sets', async () => {
    const text = await mountWithVersion('2.3.4')

    expect(text).toContain('Frontend Version:')
    expect(text).toContain('2.3.4')
  })
})
