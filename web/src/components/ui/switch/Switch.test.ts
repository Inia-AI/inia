import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import SwitchComponent from './Switch.vue'

describe('Switch', () => {
  it('forwards reka-ui update:modelValue and flips data-state on click', async () => {
    const Parent = defineComponent({
      components: { SwitchComponent },
      setup() {
        const on = ref(false)
        return { on }
      },
      template: `
        <SwitchComponent
          :model-value="on"
          @update:model-value="(v) => (on = v)"
        />
      `,
    })

    const wrapper = mount(Parent)
    const toggle = wrapper.find('button[role="switch"]')
    expect(wrapper.vm.on).toBe(false)
    expect(toggle.attributes('data-state')).toBe('unchecked')
    expect(toggle.classes()).toContain('inia-switch')

    await toggle.trigger('click')
    expect(wrapper.vm.on).toBe(true)
    expect(toggle.attributes('data-state')).toBe('checked')
  })

  it('dispatches vee-validate componentField exactly once when v-bound alongside an explicit @update:model-value', async () => {
    const onFieldUpdate = vi.fn()

    const Parent = defineComponent({
      components: { SwitchComponent },
      setup() {
        const on = ref(false)
        const componentField = { 'onUpdate:modelValue': onFieldUpdate }
        return { on, componentField }
      },
      template: `
        <SwitchComponent
          v-bind="componentField"
          :model-value="on"
          @update:model-value="(v) => (on = v)"
        />
      `,
    })

    const wrapper = mount(Parent)
    await wrapper.find('button[role="switch"]').trigger('click')

    expect(wrapper.vm.on).toBe(true)
    expect(onFieldUpdate).toHaveBeenCalledTimes(1)
    expect(onFieldUpdate).toHaveBeenCalledWith(true)
  })
})
