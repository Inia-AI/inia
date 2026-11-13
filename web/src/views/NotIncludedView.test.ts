import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import { extensionPoints } from '@/modules/extensionPoints'
import NotIncludedView from './NotIncludedView.vue'

const mountAt = async (capability: string) => {
  window.history.replaceState({ capability }, '')
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:any(.*)*', component: NotIncludedView }] })
  await router.push('/workflows/create')
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
  return mount(NotIncludedView, { global: { plugins: [router, i18n] } })
}

afterEach(() => {
  extensionPoints.notIncludedActions.length = 0
})

describe('NotIncludedView', () => {
  it('shows what a module offers for the missing capability, before the way to the Dashboard', async () => {
    extensionPoints.notIncludedActions.push(
      defineComponent({ props: ['capability'], setup: props => () => h('p', { 'data-testid': 'module-offer' }, `offer for ${props.capability}`) }),
    )
    const wrapper = await mountAt('run')

    const offer = wrapper.get('[data-testid="module-offer"]')
    expect(offer.text()).toBe('offer for run')
    expect(offer.element.compareDocumentPosition(wrapper.get('a[href="/"]').element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

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
