import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { Popover, PopoverContent, PopoverTrigger } from '.'
import { Command, CommandInput, CommandItem, CommandList } from '@/components/ui/command'

const FilterPopover = defineComponent({
  components: { Popover, PopoverContent, PopoverTrigger, Command, CommandInput, CommandItem, CommandList },
  props: { withCommand: { type: Boolean, default: false }, textOnly: { type: Boolean, default: false } },
  setup: () => ({ open: ref(false) }),
  template: `
    <main>
      <Popover v-model:open="open">
        <PopoverTrigger data-testid="trigger">Report type</PopoverTrigger>
        <PopoverContent>
          <Command v-if="withCommand">
            <CommandInput placeholder="Search..." />
            <CommandList>
              <CommandItem value="Hedge Funds Monitoring">Hedge Funds Monitoring</CommandItem>
            </CommandList>
          </Command>
          <p v-else-if="textOnly">12 values compared</p>
          <button v-else type="button">Apply</button>
        </PopoverContent>
      </Popover>
    </main>
  `,
})

const settle = async () => {
  await flushPromises()
  await new Promise(resolve => setTimeout(resolve, 10))
  await flushPromises()
}

const mounted: VueWrapper[] = []

const openPopover = async (withCommand: boolean, textOnly = false) => {
  const wrapper = mount(FilterPopover, { attachTo: document.body, props: { withCommand, textOnly } })
  mounted.push(wrapper)
  const trigger = wrapper.find('[data-testid="trigger"]').element as HTMLButtonElement
  trigger.focus()
  trigger.click()
  await settle()
  return trigger
}

const pressEscape = async () => {
  ;(document.activeElement ?? document.body).dispatchEvent(
    new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
  )
  await settle()
}

const popover = () => document.querySelector('[role="dialog"]')

afterEach(() => {
  mounted.splice(0).forEach(wrapper => wrapper.unmount())
  document.body.innerHTML = ''
})

describe('PopoverContent and Escape', () => {
  it('closes on Escape when a search list inside it holds focus, and hands focus back to the trigger', async () => {
    const trigger = await openPopover(true)
    expect(document.activeElement?.getAttribute('placeholder')).toBe('Search...')

    await pressEscape()

    expect(popover()).toBeNull()
    expect(document.activeElement).toBe(trigger)
  })

  it('keeps an Escape it handled from the window listeners that outer layers such as a dialog close on', async () => {
    await openPopover(true)
    const reachedWindow: string[] = []
    const listener = (event: KeyboardEvent) => reachedWindow.push(event.key)
    window.addEventListener('keydown', listener)

    try {
      await pressEscape()
    } finally {
      window.removeEventListener('keydown', listener)
    }

    expect(popover()).toBeNull()
    expect(reachedWindow).toEqual([])
  })

  it('closes on Escape when it holds plain content', async () => {
    const trigger = await openPopover(false)

    await pressEscape()

    expect(popover()).toBeNull()
    expect(document.activeElement).toBe(trigger)
  })
})

describe('PopoverContent with nothing to focus inside', () => {
  it('takes focus itself when it opens, and hands it back to the trigger on Escape', async () => {
    const trigger = await openPopover(false, true)

    expect(document.activeElement).toBe(popover())

    await pressEscape()

    expect(document.activeElement).toBe(trigger)
  })
})
