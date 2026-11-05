import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createI18n } from 'vue-i18n'
import { createPinia } from 'pinia'
import { EN } from '@/assets/locales/en'
import { clearAuthToken, setAuthToken } from '@/test/helpers/jwt'
import NoOrganizationView from './NoOrganizationView.vue'

const mountView = async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/no-organization', component: NoOrganizationView },
      { path: '/login', name: 'Login', component: { template: '<div/>' } },
    ],
  })
  await router.push('/no-organization')
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
  return { router, wrapper: mount(NoOrganizationView, { global: { plugins: [router, i18n, createPinia()] } }) }
}

describe('NoOrganizationView', () => {
  afterEach(() => {
    clearAuthToken()
    localStorage.removeItem('theme')
  })

  it('says the account belongs to no organization and who can add it', async () => {
    setAuthToken({ role: 'User' })
    const { wrapper } = await mountView()

    expect(wrapper.find('h1').text()).toBe('Your account has no organization')
    expect(wrapper.text()).toContain('Ask an administrator of your organization to add you')
  })

  it('shows the theme the user chose, as the rest of the app does', async () => {
    setAuthToken({ role: 'User' })
    localStorage.setItem('theme', 'light')
    document.documentElement.classList.add('dark')

    await mountView()

    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('signs the user out from its one button', async () => {
    setAuthToken({ role: 'User' })
    const { router, wrapper } = await mountView()

    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(localStorage.getItem('accessToken')).toBeNull()
    expect(router.currentRoute.value.path).toBe('/login')
  })
})
