import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock('@/components/ui/popover', async () =>
  (await import('@/test/helpers/rekaStubs')).popoverModule,
)
vi.mock('@/components/ui/command', async () =>
  (await import('@/test/helpers/rekaStubs')).commandModule,
)
vi.mock('@/components/ui/scroll-area', async () =>
  (await import('@/test/helpers/rekaStubs')).scrollAreaModule,
)
vi.mock('@lucide/vue', async () =>
  (await import('@/test/helpers/rekaStubs')).lucideModule(['Check', 'ChevronDown']),
)

import SearchableSelect from './SearchableSelect.vue'

const OPTIONS = [
  { label: 'Alpha', value: 'a' },
  { label: 'Beta', value: 'b' },
  { label: 'Any', value: '' },
]

describe('SearchableSelect', () => {
  it('renders the placeholder when nothing is selected', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, placeholder: 'Pick one' },
    })
    expect(wrapper.find('button').text()).toContain('Pick one')
  })

  it("renders the selected option's label, not its value", () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    expect(wrapper.find('button').text()).toContain('Alpha')
    expect(wrapper.find('button').text()).not.toContain('Pick one')
  })

  it('respects the disabled prop on the trigger', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, disabled: true },
    })
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })

  it('emits update:modelValue with the option value when an item is selected', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    const items = wrapper.findAll('[data-stub="CommandItem"]')
    const beta = items.find(it => it.text().trim() === 'Beta')
    expect(beta).toBeTruthy()
    await beta!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  it('renders the "" sentinel option as the selected label when modelValue is ""', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, modelValue: '' },
    })
    expect(wrapper.find('button').text()).toContain('Any')
    expect(wrapper.find('button').text()).not.toContain('Alpha')
  })

  it('emits "" (not a space) to callers when the "" sentinel option is picked', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    const items = wrapper.findAll('[data-stub="CommandItem"]')
    const anyItem = items.find(it => it.text().trim() === 'Any')
    await anyItem!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([''])
  })

  it('reacts to external v-model changes', async () => {
    const Parent = defineComponent({
      components: { SearchableSelect },
      setup() {
        const value = ref<string | undefined>('a')
        return { value, options: OPTIONS }
      },
      template: '<SearchableSelect v-model="value" :options="options" />',
    })
    const wrapper = mount(Parent)
    expect(wrapper.find('button').text()).toContain('Alpha')
    wrapper.vm.value = 'b'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('button').text()).toContain('Beta')
  })

  it('emits the typed term through v-model:search when the parent owns filtering', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, serverFiltered: true },
    })
    await wrapper.find('[data-stub="CommandInput"]').setValue('fen')
    expect(wrapper.emitted('update:search')?.at(-1)).toEqual(['fen'])
  })

  it('clears the search term when the picker closes, so reopening shows the full list', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, serverFiltered: true },
    })
    const popover = wrapper.findComponent({ name: 'Popover' })
    popover.vm.$emit('update:open', true)
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-stub="CommandInput"]').setValue('fen')
    expect(wrapper.emitted('update:search')?.at(-1)).toEqual(['fen'])
    popover.vm.$emit('update:open', false)
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:search')?.at(-1)).toEqual([''])
  })

  it('keeps the built-in filter in client mode', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS },
    })
    expect(wrapper.find('[data-stub="Command"]').attributes('ignore-filter')).toBe('false')
  })

  it('disables the built-in filter when the parent owns filtering', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, serverFiltered: true },
    })
    expect(wrapper.find('[data-stub="Command"]').attributes('ignore-filter')).toBe('true')
  })

  it('keeps the picked option listed and on the trigger after a server-narrowed list drops it', async () => {
    const Parent = defineComponent({
      components: { SearchableSelect },
      setup() {
        const value = ref<string | undefined>()
        const options = ref([
          { label: 'Alpha', value: 'a' },
          { label: 'Beta', value: 'b' },
        ])
        return { value, options }
      },
      template: '<SearchableSelect v-model="value" :options="options" server-filtered />',
    })
    const wrapper = mount(Parent)
    const beta = wrapper.findAll('[data-stub="CommandItem"]').find(it => it.text().trim() === 'Beta')
    await beta!.trigger('click')

    wrapper.vm.options = [{ label: 'Alpha', value: 'a' }]
    await wrapper.vm.$nextTick()

    const items = wrapper.findAll('[data-stub="CommandItem"]').map(it => it.text().trim())
    expect(items).toEqual(['Beta', 'Alpha'])
    expect(wrapper.find('button').text()).toContain('Beta')
  })

  it('does not duplicate the picked option while the list still holds it', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, serverFiltered: true, modelValue: 'b' },
    })
    const items = wrapper.findAll('[data-stub="CommandItem"]').map(it => it.text().trim())
    expect(items).toEqual(['Alpha', 'Beta', 'Any'])
  })

  it('drops the retained option once the selection moves elsewhere', async () => {
    const Parent = defineComponent({
      components: { SearchableSelect },
      setup() {
        const value = ref<string | undefined>('b')
        const options = ref([
          { label: 'Alpha', value: 'a' },
          { label: 'Beta', value: 'b' },
        ])
        return { value, options }
      },
      template: '<SearchableSelect v-model="value" :options="options" server-filtered />',
    })
    const wrapper = mount(Parent)
    wrapper.vm.options = [{ label: 'Alpha', value: 'a' }]
    await wrapper.vm.$nextTick()
    expect(wrapper.find('button').text()).toContain('Beta')

    wrapper.vm.value = 'a'
    await wrapper.vm.$nextTick()

    const items = wrapper.findAll('[data-stub="CommandItem"]').map(it => it.text().trim())
    expect(items).toEqual(['Alpha'])
    expect(wrapper.find('button').text()).toContain('Alpha')
  })

  it('emits the whole picked option through select', async () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: OPTIONS, modelValue: undefined },
    })
    const beta = wrapper.findAll('[data-stub="CommandItem"]').find(it => it.text().trim() === 'Beta')
    await beta!.trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual([{ label: 'Beta', value: 'b' }])
  })

  it('shows the empty-search i18n key when the option list is empty', () => {
    const wrapper = mount(SearchableSelect, {
      props: { options: [] },
    })
    expect(wrapper.text()).toContain('shared.components.multiSelect.emptySearchText')
  })
})
