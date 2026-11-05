import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import NotIncludedView from './NotIncludedView.vue'

const mountAt = async (capability: string) => {
  window.history.replaceState({ capability }, '')
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:any(.*)*', component: NotIncludedView }] })
  await router.push('/workflows/create')
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
  return mount(NotIncludedView, { global: { plugins: [router, i18n] } })
}

describe('NotIncludedView', () => {
  it('tells an account that cannot start reports where to see why', async () => {
    const wrapper = await mountAt('run')

    expect(wrapper.get('h1').text()).toBe('Not included in your account')
    expect(wrapper.text()).toContain('Your account cannot start new reports now. The Dashboard says why and what to do next.')
  })

  it('tells an account without a feature that it is not included and who to ask', async () => {
    const wrapper = await mountAt('run-logs')

    expect(wrapper.text()).toContain('Your account does not include this page. Contact support if you need it.')
    expect(wrapper.get('a').attributes('href')).toBe('/')
  })
})
