import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key} ${JSON.stringify(params)}` : key,
  }),
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
  (await import('@/test/helpers/rekaStubs')).lucideModule([
    'Check',
    'X',
    'ChevronsUpDown',
  ]),
)

import MultiSelect from './MultiSelect.vue'
import { DROPDOWN_RENDER_CAP } from '@/constants/dropdown'

const makeOptions = (count: number) =>
  Array.from({ length: count }, (_, i) => ({
    label: `Option ${String(i).padStart(4, '0')}`,
    value: `opt-${i}`,
  }))

const mountMultiSelect = (props: Record<string, unknown>) =>
  mount(MultiSelect, { props })

const renderedItems = (wrapper: ReturnType<typeof mount>) =>
  wrapper.findAll('[data-stub="CommandItem"]')

const setSearch = async (wrapper: ReturnType<typeof mount>, value: string) => {
  await wrapper.find('[data-stub="CommandInput"]').setValue(value)
}

describe('MultiSelect option rendering cap', () => {
  it('renders all options when below the cap', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(5) })
    expect(renderedItems(wrapper)).toHaveLength(5)
  })

  it('renders at most DROPDOWN_RENDER_CAP items for large option lists', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(5000) })
    expect(renderedItems(wrapper)).toHaveLength(DROPDOWN_RENDER_CAP)
  })

  it('shows how many options are hidden when the list is capped', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(5000) })
    expect(wrapper.text()).toContain(
      `shared.components.multiSelect.moreResults {"n":${5000 - DROPDOWN_RENDER_CAP}}`,
    )
  })

  it('hides the more-results row when options fit within the cap', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(5) })
    expect(wrapper.text()).not.toContain(
      'shared.components.multiSelect.moreResults',
    )
  })

  it('narrows rendered items by the search term', async () => {
    const wrapper = mountMultiSelect({ options: makeOptions(5000) })
    await setSearch(wrapper, 'option 0042')
    const items = renderedItems(wrapper)
    expect(items).toHaveLength(1)
    expect(items[0]!.text()).toContain('Option 0042')
  })

  it('matches on searchText when provided', async () => {
    const wrapper = mountMultiSelect({
      options: [
        { label: 'Jane Doe (jane@x.com)', value: 'u1', searchText: 'Jane Doe jane@x.com' },
        { label: 'John Roe (john@x.com)', value: 'u2', searchText: 'John Roe john@x.com' },
      ],
    })
    await setSearch(wrapper, 'jane@')
    const items = renderedItems(wrapper)
    expect(items).toHaveLength(1)
    expect(items[0]!.text()).toContain('Jane Doe')
  })

  it('shows the empty state only when nothing matches', async () => {
    const wrapper = mountMultiSelect({ options: makeOptions(10) })
    expect(wrapper.text()).not.toContain(
      'shared.components.multiSelect.emptySearchText',
    )
    await setSearch(wrapper, 'no such thing')
    expect(renderedItems(wrapper)).toHaveLength(0)
    expect(wrapper.text()).toContain(
      'shared.components.multiSelect.emptySearchText',
    )
  })

  it('emits the added value when a rendered item is selected', async () => {
    const wrapper = mountMultiSelect({
      options: makeOptions(10),
      modelValue: ['opt-1'],
    })
    const target = renderedItems(wrapper).find(el =>
      el.text().includes('Option 0003'),
    )
    await target!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([
      ['opt-1', 'opt-3'],
    ])
  })

  it('resolves badge labels for selected values beyond the rendered window', () => {
    const wrapper = mountMultiSelect({
      options: makeOptions(5000),
      modelValue: ['opt-4999'],
    })
    expect(wrapper.text()).toContain('Option 4999')
  })
})

describe('MultiSelect accessible name', () => {
  it('puts the name and the form id on the combobox trigger', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(3), 'aria-label': 'Report type', id: 'type-field' })
    const trigger = wrapper.find('[role="combobox"]')
    expect(trigger.attributes('aria-label')).toBe('Report type')
    expect(trigger.attributes('id')).toBe('type-field')
  })

  it('keeps a form binding\'s blur listener off the trigger, which blurs as its list opens', async () => {
    const onBlur = vi.fn()
    const wrapper = mountMultiSelect({ options: makeOptions(3), id: 'type-field', onBlur })
    const trigger = wrapper.find('[role="combobox"]')
    await trigger.trigger('blur')
    expect(onBlur).not.toHaveBeenCalled()
    expect(trigger.attributes('id')).toBe('type-field')
  })

  it('leaves the trigger unnamed by aria-label when no name is given', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(3) })
    expect(wrapper.find('[role="combobox"]').attributes('aria-label')).toBeUndefined()
  })
})

describe('MultiSelect idle text', () => {
  const trigger = (wrapper: ReturnType<typeof mount>) => wrapper.get('[role="combobox"]')

  it('says what the filter is set to when nothing is chosen', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(3), placeholder: 'All' })
    expect(trigger(wrapper).text()).toBe('All')
  })

  it('shows the chosen value instead of the idle text, and brings the idle text back when cleared', async () => {
    const wrapper = mountMultiSelect({ options: makeOptions(3), placeholder: 'All', modelValue: ['opt-1'] })
    expect(trigger(wrapper).text()).toContain('Option 0001')
    expect(trigger(wrapper).text()).not.toContain('All')

    await wrapper.setProps({ modelValue: [] })
    expect(trigger(wrapper).text()).toBe('All')
  })

  it('says how many values are chosen, not the idle text, while a chosen value matches no loaded option', () => {
    const wrapper = mountMultiSelect({ options: makeOptions(3), placeholder: 'All', modelValue: ['deleted-id'] })
    expect(trigger(wrapper).text()).not.toContain('All')
    expect(trigger(wrapper).text()).toContain('shared.components.multiSelect.nSelected {"n":1}')
  })
})
