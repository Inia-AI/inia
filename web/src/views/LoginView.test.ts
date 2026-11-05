import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { axiosError } from '@/test/helpers/axiosError'

const { login, toast } = vi.hoisted(() => ({ login: vi.fn(), toast: vi.fn() }))

vi.mock('@/api', () => ({
  default: { Authentication: { useLogin: () => ({ mutateAsync: login }) } },
}))
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast }) }))
vi.mock('@/composables/refreshToken', () => ({ useRefreshToken: () => ({ setRefreshTokenTimer: vi.fn() }) }))
vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import LoginView from './LoginView.vue'

const refusal = (status: number) => axiosError(status)

const signIn = async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', name: 'Home', component: { template: '<div />' } }],
  })
  const wrapper = mount(LoginView, { global: { plugins: [router], stubs: { LoginForm: true } } })
  wrapper.findComponent({ name: 'LoginForm' }).vm.$emit('submit', { username: 'analyst@example.com', password: 'secret' })
  await flushPromises()
}

describe('LoginView', () => {
  beforeEach(() => {
    login.mockReset()
    toast.mockReset()
  })

  it('names the sign-in limit when the API answers 429', async () => {
    login.mockRejectedValue(refusal(429))

    await signIn()

    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: 'page.login.errors.tooManyAttempts' }))
  })

  it('answers a refused password with the wrong-credentials message', async () => {
    login.mockRejectedValue(refusal(403))

    await signIn()

    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: 'page.login.errors.loginFailed' }))
  })
})
