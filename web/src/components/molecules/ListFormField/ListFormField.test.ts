import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { useForm } from 'vee-validate'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

import ListFormField from './ListFormField.vue'

const mountList = (urls: string[]) =>
  mount(
    defineComponent({
      setup() {
        useForm({ initialValues: { urls } })
        return () => h(ListFormField, { name: 'urls', label: 'URL' })
      },
    }),
    { global: { stubs: { IniaIcon: true } }, attachTo: document.body },
  )

describe('ListFormField', () => {
  it('removes a row from a native button named for that row', async () => {
    const wrapper = mountList(['https://a.example', 'https://b.example'])
    await flushPromises()

    const removes = wrapper.findAll('button[aria-label^="shared.components.listFormField.removeItem"]')
    expect(removes.map(button => button.attributes('type'))).toEqual(['button', 'button'])
    expect(removes[1]!.attributes('aria-label')).toBe('shared.components.listFormField.removeItem:{"n":2}')

    await removes[0]!.trigger('click')
    await flushPromises()

    const inputs = wrapper.findAll('input')
    expect(inputs).toHaveLength(1)
    expect((inputs[0]!.element as HTMLInputElement).value).toBe('https://b.example')
    wrapper.unmount()
  })

  it('offers no remove button while a single row is left', async () => {
    const wrapper = mountList(['https://a.example'])
    await flushPromises()

    expect(wrapper.find('button[aria-label^="shared.components.listFormField.removeItem"]').exists()).toBe(false)
    wrapper.unmount()
  })
})
