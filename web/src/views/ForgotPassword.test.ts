import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'

const { getAuthProviders } = vi.hoisted(() => ({ getAuthProviders: vi.fn() }))

vi.mock('@/api', () => ({
  default: {
    Authentication: { getAuthProviders },
    Password: { useForgotPassword: () => ({ mutateAsync: vi.fn() }) },
  },
}))
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: vi.fn() }) }))
vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import ForgotPassword from './ForgotPassword.vue'

const open = async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/login', name: 'Login', component: { template: '<div />' } }],
  })
  const wrapper = mount(ForgotPassword, {
    global: { plugins: [router], mocks: { $t: (key: string) => key }, stubs: { ForgotPasswordForm: true } },
  })
  await flushPromises()
  return wrapper
}

describe('ForgotPassword', () => {
  beforeEach(() => {
    getAuthProviders.mockReset()
  })

  it('asks for the address when the deployment can mail a reset link', async () => {
    getAuthProviders.mockResolvedValue({ providers: [], passwordReset: true })

    const wrapper = await open()

    expect(wrapper.findComponent({ name: 'ForgotPasswordForm' }).exists()).toBe(true)
  })

  it('says reset by mail is off instead of promising a mail', async () => {
    getAuthProviders.mockResolvedValue({ providers: [], passwordReset: false })

    const wrapper = await open()

    expect(wrapper.text()).toContain('page.forgotPassword.unavailable')
    expect(wrapper.findComponent({ name: 'ForgotPasswordForm' }).exists()).toBe(false)
  })
})
