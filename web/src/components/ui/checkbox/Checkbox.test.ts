import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import Checkbox from './Checkbox.vue'

describe('Checkbox', () => {
  it('forwards reka-ui update:modelValue so parent bindings fire on click', async () => {
    const Parent = defineComponent({
      components: { Checkbox },
      setup() {
        const checked = ref(false)
        return { checked }
      },
      template: `
        <Checkbox
          :model-value="checked"
          @update:model-value="(v) => (checked = v === true)"
        />
      `,
    })

    const wrapper = mount(Parent)
    const box = wrapper.find('button[role="checkbox"]')
    expect(wrapper.vm.checked).toBe(false)
    expect(box.attributes('data-state')).toBe('unchecked')

    await box.trigger('click')
    expect(wrapper.vm.checked).toBe(true)
    expect(box.attributes('data-state')).toBe('checked')

    await box.trigger('click')
    expect(wrapper.vm.checked).toBe(false)
    expect(box.attributes('data-state')).toBe('unchecked')
  })

  it('collapses indeterminate emissions to false when the consumer uses val === true', async () => {
    const Parent = defineComponent({
      components: { Checkbox },
      setup() {
        const checked = ref(false)
        return { checked }
      },
      template: `
        <Checkbox
          :model-value="checked"
          @update:model-value="(v) => (checked = v === true)"
        />
      `,
    })

    const wrapper = mount(Parent)
    wrapper.findComponent(Checkbox).vm.$emit('update:modelValue', 'indeterminate')
    expect(wrapper.vm.checked).toBe(false)

    wrapper.findComponent(Checkbox).vm.$emit('update:modelValue', true)
    expect(wrapper.vm.checked).toBe(true)
  })

  it('supports a Set-based toggle across several checkboxes', async () => {
    const Parent = defineComponent({
      components: { Checkbox },
      setup() {
        const selected = ref<Set<string>>(new Set())
        const toggle = (id: string) => {
          const next = new Set(selected.value)
          if (next.has(id)) next.delete(id)
          else next.add(id)
          selected.value = next
        }
        return { selected, toggle }
      },
      template: `
        <div>
          <Checkbox
            data-id="a"
            :model-value="selected.has('a')"
            @update:model-value="() => toggle('a')"
          />
          <Checkbox
            data-id="b"
            :model-value="selected.has('b')"
            @update:model-value="() => toggle('b')"
          />
        </div>
      `,
    })

    const wrapper = mount(Parent)
    expect([...wrapper.vm.selected]).toEqual([])

    await wrapper.find('[data-id="a"]').trigger('click')
    expect([...wrapper.vm.selected]).toEqual(['a'])

    await wrapper.find('[data-id="b"]').trigger('click')
    expect([...wrapper.vm.selected].sort()).toEqual(['a', 'b'])

    await wrapper.find('[data-id="a"]').trigger('click')
    expect([...wrapper.vm.selected]).toEqual(['b'])
  })

  it('supports the double-dispatch pattern used with vee-validate componentField', async () => {
    const onFieldUpdate = vi.fn()

    const Parent = defineComponent({
      components: { Checkbox },
      setup() {
        const value = ref(false)
        const componentField = { 'onUpdate:modelValue': onFieldUpdate }
        return { value, componentField }
      },
      template: `
        <Checkbox
          :model-value="value"
          @update:model-value="(val) => {
            const checked = val === true
            value = checked
            componentField['onUpdate:modelValue']?.(checked)
          }"
        />
      `,
    })

    const wrapper = mount(Parent)
    await wrapper.find('button[role="checkbox"]').trigger('click')
    expect(wrapper.vm.value).toBe(true)
    expect(onFieldUpdate).toHaveBeenCalledWith(true)

    wrapper.findComponent(Checkbox).vm.$emit('update:modelValue', 'indeterminate')
    expect(wrapper.vm.value).toBe(false)
    expect(onFieldUpdate).toHaveBeenLastCalledWith(false)
  })
})
