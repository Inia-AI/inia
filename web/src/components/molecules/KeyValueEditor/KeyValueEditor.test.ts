import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'

import KeyValueEditor from './KeyValueEditor.vue'

const mountControlled = (initial: Record<string, string> = {}) => {
  const model = ref<Record<string, string>>({ ...initial })

  const Parent = defineComponent({
    setup() {
      return () =>
        h(KeyValueEditor, {
          modelValue: model.value,
          'onUpdate:modelValue': (value: Record<string, string>) => {
            model.value = value
          },
          namePrefix: 'kv',
          addLabel: 'Add',
          removeLabel: 'Remove',
          duplicateKeyMessage: 'Duplicate key',
        })
    },
  })

  return { wrapper: mount(Parent), model }
}

const clickButton = async (
  wrapper: ReturnType<typeof mountControlled>['wrapper'],
  text: string,
) => {
  const button = wrapper.findAll('button').find(b => b.text() === text)
  expect(button).toBeTruthy()
  await button!.trigger('click')
  await flushPromises()
}

const inputValue = (
  wrapper: ReturnType<typeof mountControlled>['wrapper'],
  name: string,
) => (wrapper.find(`input[name="${name}"]`).element as HTMLInputElement).value

describe('KeyValueEditor', () => {
  it('renders a row per entry in the model', () => {
    const { wrapper } = mountControlled({ alpha: '1', beta: '2' })

    expect(wrapper.find('input[name="kv-key-0"]').exists()).toBe(true)
    expect(wrapper.find('input[name="kv-key-1"]').exists()).toBe(true)
    expect(inputValue(wrapper, 'kv-key-0')).toBe('alpha')
  })

  it('emits a new pair when a row is added and filled', async () => {
    const { wrapper, model } = mountControlled({})

    await clickButton(wrapper, 'Add')
    await wrapper.find('input[name="kv-key-0"]').setValue('sector')
    await wrapper.find('textarea[name="kv-value-0"]').setValue('technology')

    expect(model.value).toEqual({ sector: 'technology' })
  })

  it('keeps a newly added blank row instead of dropping it on the v-model echo', async () => {
    const { wrapper } = mountControlled({ existing: 'kept' })

    await clickButton(wrapper, 'Add')

    expect(wrapper.find('input[name="kv-key-1"]').exists()).toBe(true)
  })

  it('drops a row whose key is blank from the emitted record', async () => {
    const { wrapper, model } = mountControlled({ keep: 'yes' })

    await clickButton(wrapper, 'Add')
    await wrapper.find('textarea[name="kv-value-1"]').setValue('orphan value')

    expect(model.value).toEqual({ keep: 'yes' })
  })

  it('removes a row from the emitted record', async () => {
    const { wrapper, model } = mountControlled({ drop: 'me', keep: 'this' })

    await clickButton(wrapper, 'Remove')

    expect(model.value).toEqual({ keep: 'this' })
  })

  it('re-syncs rows when the model changes externally', async () => {
    const { wrapper, model } = mountControlled({ alpha: '1' })

    model.value = { x: '9', y: '8' }
    await flushPromises()

    expect(inputValue(wrapper, 'kv-key-0')).toBe('x')
    expect(wrapper.find('input[name="kv-key-1"]').exists()).toBe(true)
  })

  it('flags rows that share a key', async () => {
    const { wrapper } = mountControlled({})

    await clickButton(wrapper, 'Add')
    await clickButton(wrapper, 'Add')
    await wrapper.find('input[name="kv-key-0"]').setValue('sector')
    await wrapper.find('input[name="kv-key-1"]').setValue('sector')

    expect(wrapper.text()).toContain('Duplicate key')
  })

  it('flags keys that differ only by case', async () => {
    const { wrapper } = mountControlled({})

    await clickButton(wrapper, 'Add')
    await clickButton(wrapper, 'Add')
    await wrapper.find('input[name="kv-key-0"]').setValue('Sector')
    await wrapper.find('input[name="kv-key-1"]').setValue('sector')

    expect(wrapper.text()).toContain('Duplicate key')
  })

  it('collapses keys that differ only by case, last value winning', async () => {
    const { wrapper, model } = mountControlled({})

    await clickButton(wrapper, 'Add')
    await clickButton(wrapper, 'Add')
    await wrapper.find('input[name="kv-key-0"]').setValue('Sector')
    await wrapper.find('textarea[name="kv-value-0"]').setValue('first')
    await wrapper.find('input[name="kv-key-1"]').setValue('sector')
    await wrapper.find('textarea[name="kv-value-1"]').setValue('second')

    expect(model.value).toEqual({ sector: 'second' })
  })
})
