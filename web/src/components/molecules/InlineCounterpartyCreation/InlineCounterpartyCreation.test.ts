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

const OPTIONS = [
  { label: 'Alpha Holdings', value: 'a' },
  { label: 'Beta Capital', value: 'b' },
]

let wrapper: VueWrapper | null = null

async function openAndSearch(term: string) {
  wrapper = mount(CounterpartySearchableSelect, {
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
const addNewButton = () =>
  Array.from(document.body.querySelectorAll('button')).find(button => button.textContent?.includes('Add new company'))
const createDialogOpen = () => document.body.querySelector('[data-stub="CreateCounterpartyDialog"]')!.getAttribute('data-open')

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

describe('CounterpartySearchableSelect inline creation', () => {
  it('says nothing matches, and still offers to create the name, when the search finds nothing', async () => {
    await openAndSearch('Zyx keyboard probe')

    expect(document.body.textContent).toContain('No companies match "Zyx keyboard probe"')
    expect(listText()).toContain('Create "Zyx keyboard probe" as new company')
  })

  it('reaches the create option with ArrowDown and opens the create dialog with Enter', async () => {
    const input = await openAndSearch('Zyx keyboard probe')
    expect(createDialogOpen()).toBe('false')

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await flushPromises()
    const highlighted = document.body.querySelector('[role="option"][data-highlighted]')
    expect(highlighted?.textContent).toContain('Create "Zyx keyboard probe" as new company')

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flushPromises()

    expect(createDialogOpen()).toBe('true')
  })

  it('keeps the add-new button outside the list of options', async () => {
    await openAndSearch('Al')

    const addNew = addNewButton()
    expect(addNew).toBeDefined()
    expect(addNew!.closest('[role="listbox"]')).toBeNull()
    expect(addNew!.getAttribute('aria-haspopup')).toBe('dialog')
    expect(addNew!.getAttribute('aria-expanded')).toBe('false')
  })

  it('opens the create dialog from the add-new button before anything is typed', async () => {
    await openAndSearch('')
    expect(createDialogOpen()).toBe('false')

    addNewButton()!.click()
    await flushPromises()

    expect(createDialogOpen()).toBe('true')
  })
})
