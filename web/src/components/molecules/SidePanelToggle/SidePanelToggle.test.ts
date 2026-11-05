import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import SidePanelToggle from './SidePanelToggle.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

const mountToggle = (expanded: boolean) =>
  mount(SidePanelToggle, {
    props: { expanded, controls: 'info-panel' },
    global: { plugins: [i18n] },
  })

describe('SidePanelToggle', () => {
  it('names the collapse action, says the panel is open and points at it, with the arrow hidden from assistive technology', () => {
    const button = mountToggle(true).get('button')

    expect(button.attributes('type')).toBe('button')
    expect(button.attributes('aria-label')).toBe('Collapse panel')
    expect(button.attributes('title')).toBe('Collapse panel')
    expect(button.attributes('aria-expanded')).toBe('true')
    expect(button.attributes('aria-controls')).toBe('info-panel')
    expect(button.classes()).toContain('collapse-button-simple')
    const glyph = button.get('[aria-hidden="true"]')
    expect(glyph.text()).toBe('‹')
  })

  it('names the expand action and says the panel is closed, pointing at no panel while none is rendered', () => {
    const button = mountToggle(false).get('button')

    expect(button.attributes('aria-label')).toBe('Expand panel')
    expect(button.attributes('title')).toBe('Expand panel')
    expect(button.attributes('aria-expanded')).toBe('false')
    expect(button.attributes('aria-controls')).toBeUndefined()
    expect(button.classes()).toContain('expand-button-simple')
    expect(button.get('[aria-hidden="true"]').text()).toBe('›')
  })

  it('asks its owner to toggle the panel when pressed', async () => {
    const wrapper = mountToggle(true)

    await wrapper.get('button').trigger('click')

    expect(wrapper.emitted('toggle')).toHaveLength(1)
  })

  it('keeps focus on the toggle that replaces the one pressed, in both directions', async () => {
    const page = mount(
      defineComponent({
        setup() {
          const collapsed = ref(false)
          return () =>
            collapsed.value
              ? h('div', { key: 'collapsed', class: 'collapsed' }, [
                  h(SidePanelToggle, { expanded: false, controls: 'info-panel', onToggle: () => (collapsed.value = false) }),
                ])
              : h('div', { key: 'expanded', id: 'info-panel' }, [
                  h(SidePanelToggle, { expanded: true, controls: 'info-panel', onToggle: () => (collapsed.value = true) }),
                ])
        },
      }),
      { global: { plugins: [i18n] }, attachTo: document.body },
    )

    page.get('button').element.focus()
    await page.get('button').trigger('click')
    await flushPromises()
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Expand panel')

    await page.get('button').trigger('click')
    await flushPromises()
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Collapse panel')
    page.unmount()
  })

  it('leaves focus alone when a toggle for the panel mounts without being pressed', async () => {
    const outside = document.createElement('button')
    document.body.append(outside)
    outside.focus()

    const wrapper = mount(SidePanelToggle, {
      props: { expanded: false, controls: 'info-panel' },
      global: { plugins: [i18n] },
      attachTo: document.body,
    })
    await flushPromises()

    expect(document.activeElement).toBe(outside)
    wrapper.unmount()
    outside.remove()
  })
})
