import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { axiosError } from '@/test/helpers/axiosError'

const { resetPassword } = vi.hoisted(() => ({ resetPassword: vi.fn() }))

vi.mock('@/api', async () => {
  const { ref } = await vi.importActual<typeof import('vue')>('vue')
  return { default: { Password: { useResetPassword: () => ({ mutateAsync: resetPassword, isPending: ref(false) }) } } }
})
vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import ResetPassword from './ResetPassword.vue'

const EMAIL = 'analyst@example.com'
const TOKEN = 'reset+token/with=signs'
const NEW_PASSWORD = 'N3w-Passw0rd!'

const problem = (errors: Record<string, string[]>) => axiosError(400, { errors })

const open = async (query: string) => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/reset-password', name: 'ResetPassword', component: ResetPassword },
      { path: '/login', name: 'Login', component: { template: '<div />' } },
      { path: '/forgot-password', name: 'ForgotPassword', component: { template: '<div />' } },
    ],
  })
  await router.push(`/reset-password${query}`)
  const wrapper = mount(ResetPassword, { global: { plugins: [router], mocks: { $t: (key: string) => key } } })
  await flushPromises()
  return wrapper
}

const fillAndSubmit = async (wrapper: Awaited<ReturnType<typeof open>>, password: string, repeat = password) => {
  const inputs = wrapper.findAll('input[type="password"]')
  await inputs[0].setValue(password)
  await inputs[1].setValue(repeat)
  await wrapper.find('form').trigger('submit')
  await flushPromises()
}

const LINK = `?email=${encodeURIComponent(EMAIL)}&token=${encodeURIComponent(TOKEN)}`

describe('ResetPassword', () => {
  beforeEach(() => {
    resetPassword.mockReset()
  })

  it('sets the new password with the address and token from the link', async () => {
    resetPassword.mockResolvedValue(undefined)
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, NEW_PASSWORD)

    expect(resetPassword).toHaveBeenCalledWith({ email: EMAIL, token: TOKEN, newPassword: NEW_PASSWORD })
    expect(wrapper.text()).toContain('page.resetPassword.doneTitle')
  })

  it('refuses two passwords that differ before asking the API', async () => {
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, NEW_PASSWORD, 'Something-else-1')

    expect(resetPassword).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('page.profile.resetPassword.rules.mismatch')
  })

  it('says the link is spent when the API refuses it', async () => {
    resetPassword.mockRejectedValue(problem({ 'PasswordReset.InvalidLink': ['spent'] }))
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, NEW_PASSWORD)

    expect(wrapper.text()).toContain('page.resetPassword.errors.invalidLink')
    expect(wrapper.find('a[href="/forgot-password"]').exists()).toBe(true)
  })

  it('refuses a password that breaks a rule before asking the API, naming the rule', async () => {
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, 'Short1!')

    expect(resetPassword).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('page.profile.resetPassword.rules.minLength')
  })

  it('names a commonly used password in the app\'s own words', async () => {
    resetPassword.mockRejectedValue(problem({ PasswordTooCommon: ['This password is one of the most commonly used. Choose a different one.'] }))
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, 'password1')

    expect(wrapper.text()).toContain('page.profile.resetPassword.rules.tooCommon')
  })

  it('shows a password rule only the API knows, in its words', async () => {
    resetPassword.mockRejectedValue(problem({ PasswordRequiresUniqueChars: ['Passwords must use at least 2 different characters.'] }))
    const wrapper = await open(LINK)

    await fillAndSubmit(wrapper, NEW_PASSWORD)

    expect(wrapper.text()).toContain('Passwords must use at least 2 different characters.')
  })

  it('asks for a new link when the page opens without one', async () => {
    const wrapper = await open('')

    expect(wrapper.text()).toContain('page.resetPassword.errors.missingLink')
    expect(wrapper.find('form').exists()).toBe(false)
  })
})
