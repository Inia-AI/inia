import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'

import PeerCompanyMultiSelect from './PeerCompanyMultiSelect.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: EN },
  missingWarn: false,
  fallbackWarn: false,
})

const OPTIONS = [
  { label: 'Acme Holdings', value: 'ACME' },
  { label: 'Globex Corporation', value: 'GLOBEX' },
]

const settle = async () => {
  await flushPromises()
  await new Promise(resolve => setTimeout(resolve, 10))
  await flushPromises()
}

const press = async (key: string) => {
  ;(document.activeElement ?? document.body).dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
  )
  await settle()
}

const clickOn = async (element: Element) => {
  element.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, button: 0 }))
  element.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
  element.dispatchEvent(new MouseEvent('pointerup', { bubbles: true, button: 0 }))
  element.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
  await settle()
}

const searchBox = () =>
  document.querySelector<HTMLInputElement>(`input[placeholder="${i18n.global.t('shared.components.counterpartySelect.searchPrompt')}"]`)
const peerList = () => searchBox()?.closest('[role="dialog"]') ?? null
const optionNamed = (text: string) =>
  Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(option => option.textContent?.includes(text))

const mounted: VueWrapper[] = []

const openPeerList = async () => {
  const wrapper = mount(PeerCompanyMultiSelect, {
    attachTo: document.body,
    props: { options: OPTIONS, modelValue: [] },
    global: { plugins: [i18n] },
  })
  mounted.push(wrapper)
  const trigger = wrapper.find('[role="combobox"]').element as HTMLButtonElement
  trigger.focus()
  trigger.click()
  await settle()
  return { wrapper, trigger }
}

afterEach(() => {
  mounted.splice(0).forEach(wrapper => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('PeerCompanyMultiSelect list from the keyboard and pointer', () => {
  it('opens with focus in the search box', async () => {
    const { trigger } = await openPeerList()

    expect(peerList()).not.toBeNull()
    expect(document.activeElement).toBe(searchBox())
    expect(trigger.getAttribute('aria-expanded')).toBe('true')
  })

  it('closes on Escape and hands focus back to its combobox', async () => {
    const { trigger } = await openPeerList()

    await press('Escape')

    expect(peerList()).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(trigger.getAttribute('aria-expanded')).toBe('false')
  })

  it('still closes on Escape after a peer is chosen from the keyboard, and keeps the peer', async () => {
    const { wrapper, trigger } = await openPeerList()

    await press('ArrowDown')
    await press('Enter')

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['GLOBEX']])
    expect(peerList()).not.toBeNull()

    await press('Escape')

    expect(peerList()).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(wrapper.emitted('update:modelValue')).toHaveLength(1)
  })

  it('stays open while a peer is clicked, so several can be chosen, then closes on Escape', async () => {
    const { wrapper } = await openPeerList()

    await clickOn(optionNamed('Acme Holdings')!)

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['ACME']])
    expect(peerList()).not.toBeNull()

    await press('Escape')

    expect(peerList()).toBeNull()
  })

  it('closes on a click outside', async () => {
    await openPeerList()
    const outside = document.createElement('h1')
    document.body.appendChild(outside)

    await clickOn(outside)

    expect(peerList()).toBeNull()
  })

  it('closes when the combobox is clicked a second time, and reopens on the next click', async () => {
    const { trigger } = await openPeerList()

    await clickOn(trigger)
    expect(peerList()).toBeNull()

    await clickOn(trigger)
    expect(peerList()).not.toBeNull()
  })

  it('offers no way to create a company, because a peer needs a report of its own', async () => {
    await openPeerList()
    const search = searchBox()!
    search.value = 'Zyx keyboard probe'
    search.dispatchEvent(new Event('input', { bubbles: true }))
    await settle()

    const listText = document.querySelector('[role="listbox"]')?.textContent ?? ''
    expect(listText).toContain(i18n.global.t('shared.components.multiSelect.emptySearchText'))
    expect(listText).not.toContain('Zyx keyboard probe')
    expect(Array.from(document.querySelectorAll('button')).some(button => button.textContent?.includes(i18n.global.t('shared.components.counterpartySelect.addNew')))).toBe(false)
  })
})
