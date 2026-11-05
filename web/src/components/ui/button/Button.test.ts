import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'

import Button from './Button.vue'

describe('Button', () => {
  it('renders a native button that does not submit a form unless asked to', () => {
    const wrapper = mount(Button, { slots: { default: 'Cancel' } })

    expect(wrapper.element.tagName).toBe('BUTTON')
    expect(wrapper.attributes('type')).toBe('button')
  })

  it('keeps an explicit submit type', () => {
    const wrapper = mount(Button, { props: { type: 'submit' }, slots: { default: 'Submit' } })

    expect(wrapper.attributes('type')).toBe('submit')
  })

  it('puts no type on an element that is not a button', () => {
    const wrapper = mount(Button, { props: { as: 'a' }, attrs: { href: '/reports' }, slots: { default: 'Reports' } })

    expect(wrapper.element.tagName).toBe('A')
    expect(wrapper.attributes('type')).toBeUndefined()
  })

  it('puts no type on the child it renders as', () => {
    const wrapper = mount(Button, {
      props: { asChild: true },
      slots: { default: () => h('a', { href: '/reports' }, 'Reports') },
    })

    const link = wrapper.get('a')
    expect(link.attributes('href')).toBe('/reports')
    expect(link.attributes('type')).toBeUndefined()
  })
})
