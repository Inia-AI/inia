import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import MultiSelect from './MultiSelect.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: EN },
  missingWarn: false,
  fallbackWarn: false,
})

const OPTIONS = [
  { label: 'Hedge Funds Monitoring', value: 'HedgeFundsMonitoring' },
  { label: 'Private Equity Monitoring', value: 'PrivateEquityMonitoring' },
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

const popover = () => document.querySelector('[role="dialog"]')
const searchBox = () => document.querySelector<HTMLInputElement>('input[placeholder="Search..."]')

const mounted: VueWrapper[] = []

const mountFilter = async (modelValue: string[] = []) => {
  const wrapper = mount(MultiSelect, {
    attachTo: document.body,
    props: { options: OPTIONS, modelValue, 'aria-label': 'Report type' },
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

describe('MultiSelect popover from the keyboard', () => {
  it('opens with focus in the search box', async () => {
    await mountFilter()

    expect(popover()).not.toBeNull()
    expect(document.activeElement).toBe(searchBox())
  })

  it('closes on Escape and hands focus back to its combobox', async () => {
    const { trigger } = await mountFilter()

    await press('Escape')

    expect(popover()).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(trigger.getAttribute('aria-expanded')).toBe('false')
  })

  it('still closes on Escape after a value is chosen from the keyboard, and keeps the value', async () => {
    const { wrapper, trigger } = await mountFilter()

    await press('ArrowDown')
    await press('Enter')

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['PrivateEquityMonitoring']])
    expect(popover()).not.toBeNull()

    await press('Escape')

    expect(popover()).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(wrapper.emitted('update:modelValue')).toHaveLength(1)
  })

  it('leaves nothing on the page hidden from assistive technology once it closes', async () => {
    await mountFilter()

    await press('Escape')

    expect(document.querySelectorAll('[aria-hidden="true"]:not([data-reka-focus-guard])')).toHaveLength(0)
  })

  it('reopens from its combobox after Escape closed it', async () => {
    const { trigger } = await mountFilter()
    await press('Escape')

    trigger.click()
    await settle()

    expect(popover()).not.toBeNull()
  })

  it('closes when the trigger is clicked a second time', async () => {
    const { trigger } = await mountFilter()

    trigger.click()
    await settle()

    expect(popover()).toBeNull()
  })

  it('closes on a click outside', async () => {
    await mountFilter()
    const outside = document.createElement('h1')
    document.body.appendChild(outside)

    outside.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, button: 0 }))
    outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
    outside.dispatchEvent(new MouseEvent('pointerup', { bubbles: true, button: 0 }))
    outside.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
    await settle()

    expect(popover()).toBeNull()
  })

  it('stays open while an option is clicked, so several can be chosen', async () => {
    const { wrapper } = await mountFilter()
    const option = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
      element => element.textContent?.includes('Private Equity Monitoring'),
    )

    option!.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, button: 0 }))
    option!.click()
    await settle()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['PrivateEquityMonitoring']])
    expect(popover()).not.toBeNull()
  })

  it('clears the search when it closes', async () => {
    const { trigger } = await mountFilter()
    const search = searchBox()!
    search.value = 'private'
    search.dispatchEvent(new Event('input', { bubbles: true }))
    await settle()

    await press('Escape')
    trigger.click()
    await settle()

    expect(searchBox()!.value).toBe('')
    expect(document.querySelectorAll('[role="option"]')).toHaveLength(2)
  })
})

describe('MultiSelect with many choices', () => {
  const many = ['HedgeFundsMonitoring', 'PrivateEquityMonitoring']

  it('puts no control of its own inside its combobox', async () => {
    const wrapper = mount(MultiSelect, {
      attachTo: document.body,
      props: { options: OPTIONS, modelValue: many, maxBadges: 1, 'aria-label': 'Report type' },
      global: { plugins: [i18n] },
    })
    mounted.push(wrapper)

    const trigger = wrapper.get('[role="combobox"]')
    expect(trigger.findAll('button, [role="button"], [tabindex]')).toHaveLength(0)
  })

  it('offers clearing every choice from the keyboard, inside the list', async () => {
    const wrapper = mount(MultiSelect, {
      attachTo: document.body,
      props: { options: OPTIONS, modelValue: many, maxBadges: 1, 'aria-label': 'Report type' },
      global: { plugins: [i18n] },
    })
    mounted.push(wrapper)
    const trigger = wrapper.get('[role="combobox"]').element as HTMLButtonElement
    trigger.focus()
    trigger.click()
    await settle()

    const clear = [...document.querySelectorAll('[role="option"]')].find(option => option.textContent?.includes('Clear 2 selections'))
    expect(clear).toBeDefined()
    ;(clear as HTMLElement).click()
    await settle()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[]])
  })
})
