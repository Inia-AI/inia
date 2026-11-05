import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { axiosError } from '@/test/helpers/axiosError'

const { verifyMfa, sendEmailOtp, toast } = vi.hoisted(() => ({ verifyMfa: vi.fn(), sendEmailOtp: vi.fn(), toast: vi.fn() }))

vi.mock('@/api', async () => {
  const { ref } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Authentication: {
        useVerifyMfa: () => ({ mutateAsync: verifyMfa, isPending: ref(false) }),
        useSendEmailOtp: () => ({ mutateAsync: sendEmailOtp, isPending: ref(false) }),
      },
    },
  }
})
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast }) }))
vi.mock('@/composables/refreshToken', () => ({ useRefreshToken: () => ({ setRefreshTokenTimer: vi.fn() }) }))
vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import TwoFactorVerifyView from './TwoFactorVerifyView.vue'

const refusal = (status: number, error?: string) => axiosError(status, error ? { error } : {})

let router: ReturnType<typeof createRouter>

const mountView = async (methods: string[]) => {
  sessionStorage.setItem('mfaToken', 'mfa-token')
  sessionStorage.setItem('mfaAvailableMethods', JSON.stringify(methods))
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/2fa-verify', name: 'TwoFactorVerify', component: TwoFactorVerifyView },
      { path: '/login', name: 'Login', component: { template: '<div />' } },
      { path: '/', name: 'Home', component: { template: '<div />' } },
    ],
  })
  await router.push('/2fa-verify')
  const wrapper = mount(TwoFactorVerifyView, { global: { plugins: [router], mocks: { $t: (key: string) => key } } })
  await flushPromises()
  return wrapper
}

const submit = async (wrapper: Awaited<ReturnType<typeof mountView>>, code: string) => {
  await wrapper.find('input[type="text"]').setValue(code)
  await wrapper.find('input[type="text"]').trigger('keyup.enter')
  await flushPromises()
}

describe('TwoFactorVerifyView', () => {
  beforeEach(() => {
    verifyMfa.mockReset()
    sendEmailOtp.mockReset()
    toast.mockReset()
    sessionStorage.clear()
  })

  it('offers two methods as a labelled group that says which one is chosen', async () => {
    sendEmailOtp.mockResolvedValue(undefined)
    const wrapper = await mountView(['totp', 'email'])

    const group = wrapper.get('[role="group"]')
    expect(wrapper.get(`#${group.attributes('aria-labelledby')}`).text()).toBe('page.twoFactor.selectMethod')
    const pressed = () => group.findAll('button').map(button => [button.text(), button.attributes('aria-pressed')])
    expect(pressed()).toEqual([
      ['page.twoFactor.methods.totp', 'true'],
      ['page.twoFactor.methods.email', 'false'],
    ])

    await group.findAll('button')[1]!.trigger('click')
    await flushPromises()

    expect(pressed()).toEqual([
      ['page.twoFactor.methods.totp', 'false'],
      ['page.twoFactor.methods.email', 'true'],
    ])
  })

  it('returns to sign-in, saying why, when the second-factor step expired', async () => {
    verifyMfa.mockRejectedValue(refusal(401, 'invalid_mfa_token'))
    const wrapper = await mountView(['totp'])

    await submit(wrapper, '123456')

    expect(router.currentRoute.value.name).toBe('Login')
    expect(sessionStorage.getItem('mfaToken')).toBeNull()
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: 'page.twoFactor.expired' }))
  })

  it('returns to sign-in when an email code is asked for after the step expired', async () => {
    sendEmailOtp.mockRejectedValue(axiosError(401, { code: 'MfaToken.Invalid' }))
    const wrapper = await mountView(['email'])

    await wrapper.findAll('button').find(button => button.text() === 'page.twoFactor.sendCode')!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('Login')
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: 'page.twoFactor.expired' }))
  })

  it('names the sign-in limit when the API answers 429', async () => {
    verifyMfa.mockRejectedValue(refusal(429))
    const wrapper = await mountView(['totp'])

    await submit(wrapper, '123456')

    expect(wrapper.text()).toContain('page.twoFactor.tooManyAttempts')
    expect(wrapper.text()).not.toContain('page.twoFactor.invalidCode')
  })

  it('names the lockout when too many wrong codes locked the account', async () => {
    verifyMfa.mockRejectedValue(refusal(401, 'account_locked_out'))
    const wrapper = await mountView(['totp'])

    await submit(wrapper, '123456')

    expect(wrapper.text()).toContain('page.twoFactor.lockedOut')
  })

  it('still calls a wrong code a wrong code', async () => {
    verifyMfa.mockRejectedValue(refusal(401, 'invalid_code'))
    const wrapper = await mountView(['totp'])

    await submit(wrapper, '123456')

    expect(wrapper.text()).toContain('page.twoFactor.invalidCode')
  })

  it('sends a recovery code with its dash, the way it was issued', async () => {
    verifyMfa.mockRejectedValue(refusal(401, 'invalid_code'))
    const wrapper = await mountView(['recovery'])

    await submit(wrapper, 'ABCDE-12345')

    expect(verifyMfa).toHaveBeenCalledWith(expect.objectContaining({ method: 'recovery', code: 'ABCDE-12345' }))
  })

  it('sends an authenticator code without spaces or dashes', async () => {
    verifyMfa.mockRejectedValue(refusal(401, 'invalid_code'))
    const wrapper = await mountView(['totp'])

    await submit(wrapper, '123-456')

    expect(verifyMfa).toHaveBeenCalledWith(expect.objectContaining({ method: 'totp', code: '123456' }))
  })
})
