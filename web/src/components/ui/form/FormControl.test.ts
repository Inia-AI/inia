import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { useForm } from 'vee-validate'

import { FormControl, FormDescription, FormField, FormItem, FormMessage } from '.'

const mountField = ({ description, error }: { description?: string; error?: string }) =>
  mount(
    defineComponent({
      setup() {
        const form = useForm({ initialValues: { name: '' } })
        if (error) form.setFieldError('name', error)
        return () =>
          h(FormField, { name: 'name' }, () =>
            h(FormItem, null, () => [
              h(FormControl, null, () => h('input', { 'data-test': 'control' })),
              description ? h(FormDescription, null, () => description) : null,
              h(FormMessage),
            ]),
          )
      },
    }),
    { attachTo: document.body },
  )

const describedBy = async (options: { description?: string; error?: string }) => {
  const wrapper = mountField(options)
  await flushPromises()
  const control = wrapper.get('[data-test="control"]').element
  const ids = control.getAttribute('aria-describedby')?.split(/\s+/).filter(Boolean) ?? []
  return { ids, resolved: ids.map(id => document.getElementById(id)?.textContent ?? null) }
}

describe('FormControl aria-describedby', () => {
  it('points at nothing when the field has neither a description nor an error', async () => {
    expect((await describedBy({})).ids).toEqual([])
  })

  it('points at the description when the field has one', async () => {
    expect((await describedBy({ description: 'Shown to every analyst' })).resolved).toEqual(['Shown to every analyst'])
  })

  it('points at the error message while the field shows one', async () => {
    expect((await describedBy({ error: 'Required' })).resolved).toEqual(['Required'])
  })
})
