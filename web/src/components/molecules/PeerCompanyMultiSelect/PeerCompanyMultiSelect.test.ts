import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

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
    'X',
    'ChevronsUpDown',
  ]),
)

import PeerCompanyMultiSelect from './PeerCompanyMultiSelect.vue'
import { Badge } from '@/components/ui/badge'
import { DROPDOWN_RENDER_CAP } from '@/constants/dropdown'

const makeOptions = (count: number) =>
  Array.from({ length: count }, (_, i) => ({
    label: `Peer ${String(i).padStart(4, '0')}`,
    value: `p-${i}`,
  }))

const mountSelect = (props: Record<string, unknown>) =>
  mount(PeerCompanyMultiSelect, { props })

describe('PeerCompanyMultiSelect option rendering cap', () => {
  it('renders at most DROPDOWN_RENDER_CAP items for large option lists', () => {
    const wrapper = mountSelect({ options: makeOptions(3000) })
    expect(wrapper.findAll('[data-stub="CommandItem"]')).toHaveLength(
      DROPDOWN_RENDER_CAP,
    )
    expect(wrapper.text()).toContain(
      'shared.components.multiSelect.moreResults',
    )
  })

  it('narrows rendered items by the search term', async () => {
    const wrapper = mountSelect({ options: makeOptions(3000) })
    await wrapper.find('[data-stub="CommandInput"]').setValue('peer 0042')
    const items = wrapper.findAll('[data-stub="CommandItem"]:not(.inline-creation-option)')
    expect(items).toHaveLength(1)
    expect(items[0]!.text()).toContain('Peer 0042')
  })

  it('emits the added value when a rendered item is selected', async () => {
    const wrapper = mountSelect({
      options: makeOptions(10),
      modelValue: ['p-1'],
    })
    const target = wrapper
      .findAll('[data-stub="CommandItem"]')
      .find(el => el.text().includes('Peer 0003'))
    await target!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([['p-1', 'p-3']])
  })

  it('resolves badge labels for selected values beyond the rendered window', () => {
    const wrapper = mountSelect({
      options: makeOptions(3000),
      modelValue: ['p-2999'],
    })
    expect(wrapper.text()).toContain('Peer 2999')
  })
})

describe('PeerCompanyMultiSelect peer availability', () => {
  const usable = { label: 'Acme', value: 'c-1' }
  const unusable = {
    label: 'Globex',
    value: 'c-2',
    unavailableReason: 'No financial analysis yet',
  }

  const itemFor = (wrapper: ReturnType<typeof mountSelect>, label: string) =>
    wrapper
      .findAll('[data-stub="CommandItem"]')
      .find(el => el.text().includes(label))

  it('lists a counterparty that cannot contribute a peer column rather than hiding it', () => {
    const wrapper = mountSelect({ options: [usable, unusable] })

    expect(itemFor(wrapper, 'Globex')).toBeDefined()
  })

  it('disables the unusable counterparty and says why', () => {
    const wrapper = mountSelect({ options: [usable, unusable] })

    const item = itemFor(wrapper, 'Globex')!
    expect(item.attributes('data-disabled')).toBe('true')
    expect(item.text()).toContain('No financial analysis yet')
  })

  it('leaves a usable counterparty selectable', () => {
    const wrapper = mountSelect({ options: [usable, unusable] })

    expect(itemFor(wrapper, 'Acme')!.attributes('data-disabled')).toBeUndefined()
  })

  it('refuses to select an unusable counterparty', async () => {
    const wrapper = mountSelect({ options: [usable, unusable], modelValue: [] })

    await itemFor(wrapper, 'Globex')!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })

  it('still lets an already-selected unusable counterparty be removed', async () => {
    const wrapper = mountSelect({
      options: [usable, unusable],
      modelValue: ['c-2'],
    })

    const badge = wrapper
      .findAllComponents(Badge)
      .find(el => el.text().includes('Globex'))
    await badge!.trigger('click')

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[]])
  })

  it('says so when the candidates could not be loaded', () => {
    const wrapper = mountSelect({ options: [], loadFailed: true })

    expect(wrapper.text()).toContain(
      'shared.components.peerCompanySelect.loadFailed',
    )
  })

  it('says nothing about loading when the candidates arrived', () => {
    const wrapper = mountSelect({ options: [usable] })

    expect(wrapper.text()).not.toContain(
      'shared.components.peerCompanySelect.loadFailed',
    )
  })

  it('shows how current a usable counterparty’s figures are', () => {
    const wrapper = mountSelect({
      options: [{ ...usable, figures: 'Figures to Mar 2025 in USD' }],
    })

    expect(wrapper.text()).toContain('Figures to Mar 2025 in USD')
  })

  it('shows the name the report will carry when it differs', () => {
    const wrapper = mountSelect({
      options: [{ ...usable, reportedAs: 'Reported as "ACME HOLDINGS PLC"' }],
    })

    expect(wrapper.text()).toContain('Reported as "ACME HOLDINGS PLC"')
  })

  it('reports the search term so the caller can narrow the candidates server-side', async () => {
    const wrapper = mountSelect({ options: [usable] })

    await wrapper.find('[data-stub="CommandInput"]').setValue('glob')

    expect(wrapper.emitted('update:search')?.at(-1)).toEqual(['glob'])
  })
})

describe('PeerCompanyMultiSelect trigger', () => {
  it('takes the id and ARIA attributes a label or a form field hands it', () => {
    const wrapper = mount(PeerCompanyMultiSelect, {
      props: { options: makeOptions(2) },
      attrs: { id: 'peer-field', 'aria-describedby': 'peer-hint' },
    })

    const trigger = wrapper.get('[role="combobox"]')
    expect(trigger.attributes('id')).toBe('peer-field')
    expect(trigger.attributes('aria-describedby')).toBe('peer-hint')
  })

  it('shows its idle text while no peer is chosen', () => {
    const wrapper = mountSelect({ options: makeOptions(2), placeholder: 'No peers' })

    expect(wrapper.get('[role="combobox"]').text()).toBe('No peers')
  })
})
