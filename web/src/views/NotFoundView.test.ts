import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import NotFoundView from './NotFoundView.vue'

const mountAt = async (path: string) => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/:unknownAddress(.*)*', component: NotFoundView },
    ],
  })
  await router.push(path)
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
  return mount(NotFoundView, { global: { plugins: [router, i18n] } })
}

describe('NotFoundView', () => {
  it('says the address has no page and links to the Dashboard', async () => {
    const wrapper = await mountAt('/retired-area/cases')

    expect(wrapper.find('h1').text()).toBe('Page not found')
    expect(wrapper.text()).toContain('/retired-area/cases')
    expect(wrapper.find('a').attributes('href')).toBe('/')
    expect(wrapper.find('a').text()).toBe('Go to the Dashboard')
  })
})
