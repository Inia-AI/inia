import { describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick, ref, withDirectives } from 'vue'
import { mount } from '@vue/test-utils'

import { vSafeHtml } from './safeHtml'

const SCRIPTED_IMAGE = '<img src="x" onerror="window.__xss = true">'

const mountBinding = (initial: string | null | undefined, modifiers: Record<string, boolean> = {}) => {
  const html = ref(initial)
  const wrapper = mount(
    defineComponent({
      setup: () => () => withDirectives(h('div'), [[vSafeHtml, html.value, undefined, modifiers]]),
    }),
  )
  return { wrapper, html }
}

describe('vSafeHtml', () => {
  it('drops event handlers from the bound HTML', () => {
    const { wrapper } = mountBinding(`<p>Revenue</p>${SCRIPTED_IMAGE}`)

    expect(wrapper.find('p').text()).toBe('Revenue')
    expect(wrapper.find('img').exists()).toBe(true)
    expect(wrapper.html()).not.toContain('onerror')
  })

  it('drops script elements and javascript links', () => {
    const { wrapper } = mountBinding('<script>window.__xss = true</script><a href="javascript:alert(1)">open</a>')

    expect(wrapper.find('script').exists()).toBe(false)
    expect(wrapper.find('a').attributes('href')).toBeUndefined()
  })

  it('sanitises the new value when the binding changes', async () => {
    const { wrapper, html } = mountBinding('<p>first</p>')

    html.value = `<p>second</p>${SCRIPTED_IMAGE}`
    await nextTick()

    expect(wrapper.find('p').text()).toBe('second')
    expect(wrapper.html()).not.toContain('onerror')
  })

  it('renders nothing for an empty binding', () => {
    expect(mountBinding(null).wrapper.element.innerHTML).toBe('')
    expect(mountBinding(undefined).wrapper.element.innerHTML).toBe('')
  })

  it('shows an unknown tag of model output as text with the model modifier', () => {
    const { wrapper } = mountBinding(`<p>Keep <stop> as text</p>${SCRIPTED_IMAGE}`, { model: true })

    expect(wrapper.find('p').text()).toBe('Keep <stop> as text')
    expect(wrapper.html()).not.toContain('onerror')
  })

  it('renders markdown with the markdown modifier', () => {
    const { wrapper } = mountBinding(`| Year | Revenue |\n| --- | --- |\n| 2025 | 10 |\n\n${SCRIPTED_IMAGE}`, {
      markdown: true,
    })

    expect(wrapper.find('table').exists()).toBe(true)
    expect(wrapper.find('td').text()).toBe('2025')
    expect(wrapper.html()).not.toContain('onerror')
  })
})
