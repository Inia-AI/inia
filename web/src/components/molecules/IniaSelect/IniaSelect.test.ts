import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'

vi.mock('@/components/ui/select', () => {
  const passthrough = (name: string) =>
    defineComponent({
      name,
      setup(_, { slots }) {
        return () => h('div', { 'data-stub': name }, slots.default?.())
      },
    })
  return {
    Select: defineComponent({
      name: 'SelectStub',
      props: ['modelValue'],
      emits: ['update:modelValue'],
      setup(props, { slots }) {
        return () =>
          h(
            'div',
            { 'data-stub': 'Select', 'data-value': String(props.modelValue ?? '') },
            slots.default?.(),
          )
      },
    }),
    SelectContent: passthrough('SelectContent'),
    SelectGroup: passthrough('SelectGroup'),
    SelectTrigger: defineComponent({
      name: 'SelectTriggerStub',
      setup(_, { slots }) {
        return () => h('button', { 'data-stub': 'SelectTrigger' }, slots.default?.())
      },
    }),
    SelectValue: defineComponent({
      name: 'SelectValueStub',
      props: ['placeholder'],
      setup(props, { slots }) {
        return () =>
          h(
            'span',
            { 'data-stub': 'SelectValue', 'data-placeholder': props.placeholder ?? '' },
            slots.default?.(),
          )
      },
    }),
    SelectItem: defineComponent({
      name: 'SelectItemStub',
      props: ['value', 'label'],
      emits: ['select'],
      setup(props, { slots, emit }) {
        return () =>
          h(
            'div',
            {
              'data-stub': 'SelectItem',
              'data-value': props.value,
              role: 'option',
              onClick: () => emit('select', props.value),
            },
            slots.default?.(),
          )
      },
    }),
  }
})

import IniaSelect from './IniaSelect.vue'

const OPTIONS = [
  { label: 'Alpha', value: 'a' },
  { label: 'Beta', value: 'b' },
  { label: 'Any', value: '' },
]

describe('IniaSelect', () => {
  it('puts the name and the form id on the trigger, where assistive technology reads them', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS },
      attrs: { 'aria-label': 'Category', id: 'category-field' },
    })
    const trigger = wrapper.find('[data-stub="SelectTrigger"]')
    expect(trigger.attributes('aria-label')).toBe('Category')
    expect(trigger.attributes('id')).toBe('category-field')
    expect(wrapper.find('[data-stub="Select"]').attributes('aria-label')).toBeUndefined()
  })

  it('keeps a form binding\'s blur listener off the trigger, which blurs as its list opens', async () => {
    const onBlur = vi.fn()
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS },
      attrs: { id: 'category-field', name: 'category', onBlur },
    })
    const trigger = wrapper.find('[data-stub="SelectTrigger"]')
    await trigger.trigger('blur')
    expect(onBlur).not.toHaveBeenCalled()
    expect(trigger.attributes('id')).toBe('category-field')
    expect(trigger.attributes('name')).toBeUndefined()
  })

  it('passes the placeholder through to SelectValue', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, placeholder: 'Pick one' },
    })
    expect(
      wrapper.find('[data-stub="SelectValue"]').attributes('data-placeholder'),
    ).toBe('Pick one')
  })

  it('renders the selected option label inside the SelectValue slot', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: 'b' },
    })
    expect(wrapper.find('[data-stub="SelectValue"]').text()).toContain('Beta')
  })

  it('renders one SelectItem per option', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    expect(wrapper.findAll('[data-stub="SelectItem"]')).toHaveLength(OPTIONS.length)
  })

  it('maps the "" sentinel option to a space for the underlying SelectItem (reka-ui rejects empty strings)', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    const values = wrapper
      .findAll('[data-stub="SelectItem"]')
      .map(el => el.attributes('data-value'))
    expect(values).toContain(' ')
    expect(values).not.toContain('')
  })

  it('passes the space-normalised value to the Select v-model when modelValue is ""', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: '' },
    })
    expect(wrapper.find('[data-stub="Select"]').attributes('data-value')).toBe(' ')
  })

  it('emits "" back to callers when the v-model is set to the space sentinel', async () => {
    const Parent = defineComponent({
      components: { IniaSelect },
      setup() {
        const value = ref<string | undefined>('a')
        return { value, options: OPTIONS }
      },
      template: '<IniaSelect v-model="value" :options="options" />',
    })
    const wrapper = mount(Parent)
    const select = wrapper.findComponent({ name: 'SelectStub' })
    select.vm.$emit('update:modelValue', ' ')
    await wrapper.vm.$nextTick()
    expect(wrapper.vm.value).toBe('')
  })

  it('renders nothing in the value slot when modelValue does not match any option and there is no placeholder', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: 'does-not-exist' },
    })
    expect(wrapper.find('[data-stub="SelectValue"]').text()).toBe('')
  })

  it('renders the placeholder in the value slot while nothing is selected (reka renders the slot instead of its placeholder prop)', () => {
    const wrapper = mount(IniaSelect, {
      props: { options: OPTIONS, modelValue: undefined, placeholder: 'Pick one' },
    })
    expect(wrapper.find('[data-stub="SelectValue"]').text()).toBe('Pick one')
  })

  it('renders empty options list without crashing', () => {
    const wrapper = mount(IniaSelect, { props: { options: [] } })
    expect(wrapper.findAll('[data-stub="SelectItem"]')).toHaveLength(0)
  })
})
