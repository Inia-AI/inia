import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { axiosError } from '@/test/helpers/axiosError'

const { changePassword, toast, REQUEST } = vi.hoisted(() => ({
  changePassword: vi.fn(),
  toast: vi.fn(),
  REQUEST: { currentPassword: 'Old-Passw0rd', newPassword: 'N3w-Passw0rd' },
}))

vi.mock('@/api', () => ({ default: { Profile: { useChangePassword: () => ({ mutateAsync: changePassword }) } } }))
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast, toasts: [], dismiss: () => {} }) }))
vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))
vi.mock('@/components/organisms', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')
  return {
    ProfileDetails: defineComponent({ render: () => h('div') }),
    PasswordResetForm: defineComponent({
      emits: ['submit'],
      setup: (_, { emit }) => () => h('button', { class: 'change-password', onClick: () => emit('submit', REQUEST) }),
    }),
  }
})
vi.mock('@/components/ui/tabs', async () => {
  const { defineComponent, h } = await vi.importActual<typeof import('vue')>('vue')
  const passThrough = defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) })
  return { Tabs: passThrough, TabsContent: passThrough, TabsList: passThrough, TabsTrigger: passThrough }
})

import ProfileView from './ProfileView.vue'

const SIGN_IN_KEYS = ['accessToken', 'refreshToken', 'expiration']

const open = async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/profile', name: 'Profile', component: ProfileView },
      { path: '/login', name: 'Login', component: { template: '<div />' } },
    ],
  })
  await router.push('/profile')
  const wrapper = mount(ProfileView, { global: { plugins: [router], mocks: { $t: (key: string) => key } } })
  await flushPromises()
  return { wrapper, router }
}

describe('ProfileView', () => {
  beforeEach(() => {
    changePassword.mockReset()
    toast.mockReset()
    SIGN_IN_KEYS.forEach(key => localStorage.setItem(key, 'stored'))
  })

  it('sends the user to sign in again after the password changes, because the change ends every session', async () => {
    changePassword.mockResolvedValue(undefined)
    const { wrapper, router } = await open()

    await wrapper.find('.change-password').trigger('click')
    await flushPromises()

    expect(changePassword).toHaveBeenCalledWith(REQUEST)
    expect(SIGN_IN_KEYS.map(key => localStorage.getItem(key))).toEqual([null, null, null])
    expect(router.currentRoute.value.name).toBe('Login')
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: 'page.profile.passwordUpdated' }))
  })

  it('keeps the user signed in when the API refuses the change', async () => {
    changePassword.mockRejectedValue(axiosError(400, { errors: { PasswordMismatch: ['wrong'] } }))
    const { wrapper, router } = await open()

    await wrapper.find('.change-password').trigger('click')
    await flushPromises()

    expect(SIGN_IN_KEYS.map(key => localStorage.getItem(key))).toEqual(['stored', 'stored', 'stored'])
    expect(router.currentRoute.value.name).toBe('Profile')
  })
})
