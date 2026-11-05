import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'

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
  (await import('@/test/helpers/rekaStubs')).lucideModule([
    'Check',
    'ChevronDown',
    'Pencil',
    'Plus',
  ]),
)

vi.mock('@/components/molecules/CreateCounterpartyDialog/CreateCounterpartyDialog.vue', () => ({
  default: defineComponent({
    name: 'CreateCounterpartyDialogStub',
    props: ['open', 'initialName', 'counterpartyTypeIds'],
    emits: ['update:open', 'created'],
    setup(props, { emit }) {
      return () =>
        h(
          'div',
          {
            'data-stub': 'CreateCounterpartyDialog',
            'data-open': String(props.open),
            'data-initial-name': props.initialName ?? '',
          },
          [
            h(
              'button',
              {
                'data-stub-action': 'simulate-create',
                onClick: () =>
                  emit('created', { id: 'new-id', name: 'New CP' }),
              },
              'create',
            ),
          ],
        )
    },
  }),
}))

vi.mock('@/components/molecules/EditCounterpartyDialog/EditCounterpartyDialog.vue', () => ({
  default: defineComponent({
    name: 'EditCounterpartyDialogStub',
    props: ['open', 'counterpartyId'],
    emits: ['update:open', 'updated'],
    setup(props) {
      return () =>
        h('div', {
          'data-stub': 'EditCounterpartyDialog',
          'data-open': String(props.open),
          'data-counterparty-id': props.counterpartyId ?? '',
        })
    },
  }),
}))

import CounterpartySearchableSelect from './CounterpartySearchableSelect.vue'
import { DROPDOWN_RENDER_CAP } from '@/constants/dropdown'

const OPTIONS = [
  { label: 'Alpha', value: 'a' },
  { label: 'Beta', value: 'b' },
  { label: 'Any', value: '' },
]

const makeOptions = (count: number) =>
  Array.from({ length: count }, (_, i) => ({
    label: `Firm ${String(i).padStart(4, '0')}`,
    value: `f-${i}`,
  }))

const setSearch = async (
  wrapper: ReturnType<typeof mount>,
  value: string,
) => {
  const input = wrapper.find('[data-stub="CommandInput"]')
  await input.setValue(value)
}

describe('CounterpartySearchableSelect', () => {
  it('renders the placeholder when nothing is selected', () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS, placeholder: 'Choose…' },
    })
    expect(wrapper.find('button').text()).toContain('Choose…')
  })

  it("renders the selected option's label, not its value", () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    expect(wrapper.find('button').text()).toContain('Alpha')
  })

  it('respects the disabled prop on the trigger', () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS, disabled: true },
    })
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })

  it('emits update:modelValue with the option value when an item is selected', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    const beta = wrapper
      .findAll('[data-stub="CommandItem"]')
      .find(el => el.text().trim() === 'Beta')
    await beta!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  it('emits "" (not " ") when the "" sentinel option is picked', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS, modelValue: 'a' },
    })
    const anyItem = wrapper
      .findAll('[data-stub="CommandItem"]')
      .find(el => el.text().trim() === 'Any')
    await anyItem!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([''])
  })

  it('renders the inline-create footer by default (search empty)', () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS },
    })
    expect(wrapper.text()).toContain('shared.components.counterpartySelect.addNew')
  })

  it('hides the footer once the search term reaches the create-option threshold', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS },
    })
    expect(wrapper.text()).toContain('shared.components.counterpartySelect.addNew')
    await setSearch(wrapper, 'foo')
    expect(wrapper.text()).not.toContain('shared.components.counterpartySelect.addNew')
  })

  it('shows an in-list create option when the search has no matches and is long enough', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS },
    })
    await setSearch(wrapper, 'novel')
    expect(wrapper.text()).toContain(
      'shared.components.counterpartySelect.createNoMatch',
    )
  })

  it('passes the (new) counterparty back as the v-model when CreateCounterpartyDialog emits "created"', async () => {
    const Parent = defineComponent({
      components: { CounterpartySearchableSelect },
      setup() {
        const value = ref<string | undefined>('')
        return { value, options: OPTIONS }
      },
      template:
        '<CounterpartySearchableSelect v-model="value" :options="options" />',
    })
    const wrapper = mount(Parent)
    const dialogStub = wrapper.find('[data-stub="CreateCounterpartyDialog"]')
    expect(dialogStub.exists()).toBe(true)
    await dialogStub.find('button').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.vm.value).toBe('new-id')
  })

  it('threads the search term into the create dialog as initialName when it opens', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS },
    })
    await setSearch(wrapper, 'novel name')
    const dialogStub = wrapper.find('[data-stub="CreateCounterpartyDialog"]')
    expect(dialogStub.attributes('data-initial-name')).toBe('novel name')
  })

  it('renders at most DROPDOWN_RENDER_CAP items for large option lists', () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: makeOptions(3000) },
    })
    expect(wrapper.findAll('[data-stub="CommandItem"]')).toHaveLength(
      DROPDOWN_RENDER_CAP,
    )
    expect(wrapper.text()).toContain(
      'shared.components.multiSelect.moreResults',
    )
  })

  it('narrows rendered items by the search term and lifts the cap row when few match', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: makeOptions(3000) },
    })
    await setSearch(wrapper, 'firm 0042')
    const items = wrapper.findAll('[data-stub="CommandItem"]:not(.inline-creation-option)')
    expect(items).toHaveLength(1)
    expect(items[0]!.text()).toContain('Firm 0042')
    expect(wrapper.text()).not.toContain(
      'shared.components.multiSelect.moreResults',
    )
  })

  it('still resolves the selected label when it falls beyond the rendered window', () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: makeOptions(3000), modelValue: 'f-2999' },
    })
    expect(wrapper.find('button').text()).toContain('Firm 2999')
  })

  it('treats a whitespace-only search term as no filter', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: { options: OPTIONS },
    })
    await setSearch(wrapper, '   ')
    expect(wrapper.findAll('[data-stub="CommandItem"]')).toHaveLength(
      OPTIONS.length,
    )
    expect(wrapper.text()).toContain(
      'shared.components.counterpartySelect.addNew',
    )
    expect(wrapper.text()).not.toContain(
      'shared.components.counterpartySelect.createNoMatch',
    )
  })

  it('matches on searchText when provided', async () => {
    const wrapper = mount(CounterpartySearchableSelect, {
      props: {
        options: [
          { label: 'Alpha', value: 'a', searchText: 'Alpha AG Zürich' },
          { label: 'Beta', value: 'b' },
        ],
      },
    })
    await setSearch(wrapper, 'zürich')
    const items = wrapper.findAll('[data-stub="CommandItem"]:not(.inline-creation-option)')
    expect(items).toHaveLength(1)
    expect(items[0]!.text()).toContain('Alpha')
  })
})
