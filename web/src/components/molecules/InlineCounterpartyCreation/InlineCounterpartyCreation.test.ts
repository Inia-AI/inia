import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'

vi.mock('@/components/molecules/CreateCounterpartyDialog/CreateCounterpartyDialog.vue', () => ({
  default: defineComponent({
    name: 'CreateCounterpartyDialogStub',
    props: ['open', 'initialName', 'counterpartyTypeIds'],
    setup: props => () => h('div', { 'data-stub': 'CreateCounterpartyDialog', 'data-open': String(props.open) }),
  }),
}))

vi.mock('@/components/molecules/EditCounterpartyDialog/EditCounterpartyDialog.vue', () => ({
  default: defineComponent({ name: 'EditCounterpartyDialogStub', setup: () => () => h('div') }),
}))

import CounterpartySearchableSelect from '@/components/molecules/CounterpartySearchableSelect/CounterpartySearchableSelect.vue'
import PeerCompanyMultiSelect from '@/components/molecules/PeerCompanyMultiSelect/PeerCompanyMultiSelect.vue'

const OPTIONS = [
  { label: 'Alpha Holdings', value: 'a' },
  { label: 'Beta Capital', value: 'b' },
]

const selects = [
  ['CounterpartySearchableSelect', CounterpartySearchableSelect],
  ['PeerCompanyMultiSelect', PeerCompanyMultiSelect],
] as const

let wrapper: VueWrapper | null = null

async function openAndSearch(component: (typeof selects)[number][1], term: string) {
  wrapper = mount(component, {
    attachTo: document.body,
    props: { options: OPTIONS },
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
    },
  })
  await wrapper.get('[role="combobox"]').trigger('click')
  await flushPromises()
  const input = document.body.querySelector<HTMLInputElement>('input[placeholder^="Type the company name"]')!
  input.focus()
  input.value = term
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await flushPromises()
  return input
}

const listText = () => document.body.querySelector('[role="listbox"]')?.textContent ?? ''
const createDialogOpen = () => document.body.querySelector('[data-stub="CreateCounterpartyDialog"]')!.getAttribute('data-open')

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

describe.each(selects)('%s inline creation', (_, component) => {
  it('says nothing matches, and still offers to create the name, when the search finds nothing', async () => {
    await openAndSearch(component, 'Zyx keyboard probe')

    expect(document.body.textContent).toContain('No companies match "Zyx keyboard probe"')
    expect(listText()).toContain('Create "Zyx keyboard probe" as new company')
  })

  it('reaches the create option with ArrowDown and opens the create dialog with Enter', async () => {
    const input = await openAndSearch(component, 'Zyx keyboard probe')
    expect(createDialogOpen()).toBe('false')

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await flushPromises()
    const highlighted = document.body.querySelector('[role="option"][data-highlighted]')
    expect(highlighted?.textContent).toContain('Create "Zyx keyboard probe" as new company')

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flushPromises()

    expect(createDialogOpen()).toBe('true')
  })

  it('keeps the add-new hint outside the list of options', async () => {
    await openAndSearch(component, 'Al')

    const hint = Array.from(document.body.querySelectorAll('p')).find(p =>
      p.textContent?.includes('Add new company'),
    )
    expect(hint).toBeDefined()
    expect(hint!.closest('[role="listbox"]')).toBeNull()
  })
})
