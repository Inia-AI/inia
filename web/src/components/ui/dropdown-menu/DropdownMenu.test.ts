import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent } from 'vue'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '.'

const chooseHedgeFunds = vi.fn()
const choosePrivateEquity = vi.fn()

const Menu = defineComponent({
  components: { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger },
  setup: () => ({ chooseHedgeFunds, choosePrivateEquity }),
  template: `
    <main data-testid="page">
      <DropdownMenu>
        <DropdownMenuTrigger data-testid="trigger">Monitoring</DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuItem @select="chooseHedgeFunds">Hedge Fund Monitoring</DropdownMenuItem>
          <DropdownMenuItem @click="choosePrivateEquity">Private Equity Monitoring</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <a href="/documents">Documents</a>
    </main>
  `,
})

const settle = async () => {
  await flushPromises()
  await new Promise(resolve => setTimeout(resolve, 10))
  await flushPromises()
}

const press = async (key: string, options: KeyboardEventInit = {}) => {
  const target = document.activeElement ?? document.body
  target.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options }),
  )
  await settle()
}

const focused = () => document.activeElement as HTMLElement

const mounted: VueWrapper[] = []

const mountMenu = async () => {
  const wrapper = mount(Menu, { attachTo: document.body })
  mounted.push(wrapper)
  const trigger = wrapper.find('[data-testid="trigger"]').element as HTMLElement
  trigger.focus()
  await settle()
  return { wrapper, trigger }
}

const menu = () => document.querySelector('[role="menu"]')

const hiddenFromAssistiveTechnology = () =>
  Array.from(document.querySelectorAll('[aria-hidden="true"]')).filter(
    element => !element.hasAttribute('data-reka-focus-guard'),
  )

afterEach(() => {
  mounted.splice(0).forEach(wrapper => wrapper.unmount())
  chooseHedgeFunds.mockReset()
  choosePrivateEquity.mockReset()
  document.body.innerHTML = ''
})

describe('DropdownMenu from the keyboard', () => {
  it('moves focus onto the first item when ArrowDown opens the menu', async () => {
    await mountMenu()

    await press('ArrowDown')

    expect(menu()).not.toBeNull()
    expect(focused().getAttribute('role')).toBe('menuitem')
    expect(focused().hasAttribute('data-highlighted')).toBe(true)
    expect(focused().textContent?.trim()).toBe('Hedge Fund Monitoring')
  })

  it('moves focus onto the first item when Enter opens the menu', async () => {
    await mountMenu()

    await press('Enter')

    expect(focused().getAttribute('role')).toBe('menuitem')
    expect(focused().textContent?.trim()).toBe('Hedge Fund Monitoring')
  })

  it('moves between the items with the arrow keys', async () => {
    await mountMenu()
    await press('ArrowDown')

    await press('ArrowDown')

    expect(focused().textContent?.trim()).toBe('Private Equity Monitoring')
    expect(focused().hasAttribute('data-highlighted')).toBe(true)

    await press('ArrowUp')

    expect(focused().textContent?.trim()).toBe('Hedge Fund Monitoring')
  })

  it('activates the highlighted item with Enter, whether it listens for select or for click', async () => {
    await mountMenu()
    await press('ArrowDown')

    await press('Enter')

    expect(chooseHedgeFunds).toHaveBeenCalledTimes(1)
    expect(menu()).toBeNull()

    await press('ArrowDown')
    await press('ArrowDown')
    await press('Enter')

    expect(choosePrivateEquity).toHaveBeenCalledTimes(1)
  })

  it('closes on Escape, returns focus to the trigger and leaves the page exposed', async () => {
    const { trigger } = await mountMenu()
    await press('ArrowDown')

    await press('Escape')

    expect(menu()).toBeNull()
    expect(focused()).toBe(trigger)
    expect(hiddenFromAssistiveTechnology()).toEqual([])
    expect(chooseHedgeFunds).not.toHaveBeenCalled()
  })

  it('closes on Tab, returns focus to the trigger and leaves the page exposed', async () => {
    const { trigger } = await mountMenu()
    await press('ArrowDown')

    await press('Tab')

    expect(menu()).toBeNull()
    expect(focused()).toBe(trigger)
    expect(hiddenFromAssistiveTechnology()).toEqual([])
  })

  it('closes on Shift+Tab and returns focus to the trigger', async () => {
    const { trigger } = await mountMenu()
    await press('ArrowDown')

    await press('Tab', { shiftKey: true })

    expect(menu()).toBeNull()
    expect(focused()).toBe(trigger)
  })
})
