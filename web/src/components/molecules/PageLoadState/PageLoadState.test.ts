import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PageLoadState from './PageLoadState.vue'

describe('PageLoadState', () => {
  it('heads a loading page with its title and shows the loader', () => {
    const wrapper = mount(PageLoadState, { props: { title: 'Validation run', loading: true } })

    expect(wrapper.get('h1').text()).toBe('Validation run')
    expect(wrapper.get('h1').classes()).toContain('sr-only')
    expect(wrapper.findComponent({ name: 'IniaLoader' }).exists()).toBe(true)
  })

  it('heads a failed page with its title and shows the message it is given', () => {
    const wrapper = mount(PageLoadState, {
      props: { title: 'Validation run' },
      slots: { default: '<p class="banner">This run could not be loaded.</p>' },
    })

    expect(wrapper.get('h1').text()).toBe('Validation run')
    expect(wrapper.get('.banner').text()).toBe('This run could not be loaded.')
    expect(wrapper.findComponent({ name: 'IniaLoader' }).exists()).toBe(false)
  })
})
