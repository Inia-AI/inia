import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EN } from '@/assets/locales/en'
import { focusTrigger, focused, mountLayout, press, pushMock, settle } from '@/test/helpers/layoutMenus'

vi.mock('vue-router', async () => (await import('@/test/helpers/layoutMenus')).vueRouterModule)
vi.mock('@/api', async () => (await import('@/test/helpers/layoutMenus')).apiModule)
vi.mock('@/composables/useMyReviewCounts', async () => (await import('@/test/helpers/layoutMenus')).reviewCountsModule)
vi.mock('@/lib/utils', async importOriginal =>
  (await import('@/test/helpers/layoutMenus')).superAdminUtilsModule(await importOriginal<typeof import('@/lib/utils')>()),
)

import DefaultLayout from './DefaultLayout.vue'
import { defineComponent, h } from 'vue'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { extensionPoints } from '@/modules/extensionPoints'

beforeEach(() => {
  pushMock.mockReset()
  document.body.innerHTML = ''
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('DefaultLayout module pieces', () => {
  afterEach(() => {
    extensionPoints.userMenuItems.length = 0
    extensionPoints.overlays.length = 0
  })

  it('lists each account menu item a module registers, after Profile', async () => {
    extensionPoints.userMenuItems.push(defineComponent({ setup: () => () => h(DropdownMenuItem, () => 'Module item') }))
    const wrapper = await mountLayout(DefaultLayout, EN)
    await focusTrigger(wrapper, 'Open account menu')

    await press('ArrowDown')

    expect([...document.querySelectorAll('[role="menuitem"]')].map(item => item.textContent?.trim())).toEqual(['Profile', 'Module item', 'Logout'])
  })

  it('mounts each overlay a module registers once, with the page', async () => {
    extensionPoints.overlays.push(defineComponent({ setup: () => () => h('span', { 'data-testid': 'module-overlay' }) }))

    const wrapper = await mountLayout(DefaultLayout, EN)

    expect(wrapper.findAll('[data-testid="module-overlay"]')).toHaveLength(1)
  })
})

describe('DefaultLayout header menus from the keyboard', () => {
  it('offers no Monitoring menu without the hedge-fund module', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)

    expect(wrapper.findAll('[aria-haspopup="menu"]').map(button => button.text())).not.toContain('Monitoring')
  })

  it('opens the account menu on ArrowDown with focus on Profile, and Escape hands focus back', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)
    const trigger = await focusTrigger(wrapper, 'Open account menu')

    await press('ArrowDown')

    expect(focused().getAttribute('role')).toBe('menuitem')
    expect(focused().textContent?.trim()).toBe('Profile')

    await press('Escape')

    expect(document.querySelector('[role="menu"]')).toBeNull()
    expect(focused()).toBe(trigger)
    expect(pushMock).not.toHaveBeenCalled()
  })
})

const panels = [
  { name: 'Tools drawer', panel: '.desktop-drawer', opener: 'Open tools menu', firstControl: 'Close tools menu', menuFits: true },
  { name: 'navigation sidebar', panel: '.mobile-sidebar', opener: 'Open navigation menu', firstControl: 'Go to dashboard', menuFits: false },
]

const HEADER_CONTROLS = { start: 150, end: 330, menu: 650 }
const WIDE_ENOUGH_FOR_THE_MENU = 1440
const TOO_NARROW_FOR_THE_MENU = 1024

// jsdom lays nothing out and has no ResizeObserver, so the header's widths are given here, and the
// stand-in observer reports on observe, as a browser's does, and on a resize announced by hand.
const stubHeaderWidth = (initialWidth: number) => {
  let headerWidth = initialWidth
  const announceResize: (() => void)[] = []
  const widthOf = (element: Element) =>
    element.matches('header.header')
      ? headerWidth
      : element.matches('.header-left')
        ? HEADER_CONTROLS.start
        : element.matches('.desktop-nav-items')
          ? HEADER_CONTROLS.menu
          : element.matches('.header-actions')
            ? HEADER_CONTROLS.end
            : 0
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const width = widthOf(this)
    return { width, height: 0, top: 0, left: 0, right: width, bottom: 0, x: 0, y: 0, toJSON: () => ({}) }
  })
  vi.stubGlobal(
    'ResizeObserver',
    class {
      private readonly announce: () => void
      constructor(callback: ResizeObserverCallback) {
        this.announce = () => callback([], this as unknown as ResizeObserver)
        announceResize.push(this.announce)
      }
      observe() {
        this.announce()
      }
      unobserve() {}
      disconnect() {}
    },
  )
  return {
    resizeTo: (width: number) => {
      headerWidth = width
      announceResize.forEach(announce => announce())
    },
  }
}

const shown = (wrapper: Awaited<ReturnType<typeof mountLayout>>, name: string) => {
  const button = wrapper.find(`button[aria-label="${name}"]`)
  return button.exists() && button.isVisible()
}

describe('DefaultLayout menu in the header', () => {
  it('collapses into Open navigation menu when it would run under the controls beside it', async () => {
    stubHeaderWidth(TOO_NARROW_FOR_THE_MENU)
    const wrapper = await mountLayout(DefaultLayout, EN)

    expect(shown(wrapper, 'Open navigation menu')).toBe(true)
    expect(shown(wrapper, 'Open tools menu')).toBe(false)
    expect(wrapper.get('.desktop-nav').attributes('inert')).toBeDefined()
  })

  it('shows itself and Open tools menu, and not Open navigation menu, when it fits', async () => {
    stubHeaderWidth(WIDE_ENOUGH_FOR_THE_MENU)
    const wrapper = await mountLayout(DefaultLayout, EN)

    expect(shown(wrapper, 'Open navigation menu')).toBe(false)
    expect(shown(wrapper, 'Open tools menu')).toBe(true)
    expect(wrapper.get('.desktop-nav').attributes('inert')).toBeUndefined()
  })

  it('collapses as the window narrows, and comes back as it widens', async () => {
    const header = stubHeaderWidth(WIDE_ENOUGH_FOR_THE_MENU)
    const wrapper = await mountLayout(DefaultLayout, EN)

    header.resizeTo(TOO_NARROW_FOR_THE_MENU)
    await settle()
    expect(shown(wrapper, 'Open navigation menu')).toBe(true)

    header.resizeTo(WIDE_ENOUGH_FOR_THE_MENU)
    await settle()
    expect(shown(wrapper, 'Open navigation menu')).toBe(false)
  })
})

const openWithKeyboard = async (wrapper: Awaited<ReturnType<typeof mountLayout>>, opener: string) => {
  const trigger = wrapper.get(`button[aria-label="${opener}"]`).element as HTMLButtonElement
  trigger.focus()
  trigger.click()
  await settle()
  return trigger
}

describe.each(panels)('DefaultLayout $name from the keyboard', ({ panel, opener, firstControl, menuFits }) => {
  let header: ReturnType<typeof stubHeaderWidth>
  beforeEach(() => {
    header = stubHeaderWidth(menuFits ? WIDE_ENOUGH_FOR_THE_MENU : TOO_NARROW_FOR_THE_MENU)
  })
  it('keeps every control of the closed panel out of the tab order', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)

    expect(wrapper.get(panel).attributes('inert')).toBeDefined()
  })

  it('puts focus on the first control of the panel when it opens, and shuts the page behind it', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)

    await openWithKeyboard(wrapper, opener)

    expect(wrapper.get(panel).attributes('inert')).toBeUndefined()
    expect(wrapper.get(panel).element.contains(focused())).toBe(true)
    expect(focused().getAttribute('aria-label')).toBe(firstControl)
    expect(wrapper.get('.main-container').attributes('inert')).toBeDefined()
  })

  it('hands focus back to the button that opened the panel when Escape closes it', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)
    const trigger = await openWithKeyboard(wrapper, opener)

    await press('Escape')

    expect(wrapper.get(panel).attributes('inert')).toBeDefined()
    expect(wrapper.get('.main-container').attributes('inert')).toBeUndefined()
    expect(focused()).toBe(trigger)
  })

  it('stays open when the menu collapses or comes back, and Escape hands focus to the menu button', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)
    await openWithKeyboard(wrapper, opener)

    header.resizeTo(menuFits ? TOO_NARROW_FOR_THE_MENU : WIDE_ENOUGH_FOR_THE_MENU)
    await settle()
    expect(wrapper.get(panel).attributes('inert')).toBeUndefined()

    await press('Escape')

    expect(wrapper.get(panel).attributes('inert')).toBeDefined()
    expect(wrapper.get('.main-container').attributes('inert')).toBeUndefined()
    expect(focused()).toBe(wrapper.get('.hamburger-trigger').element)
  })
})

describe('DefaultLayout Tools drawer', () => {
  it('announces itself as the Tools dialog', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)
    const drawer = wrapper.get('.desktop-drawer')

    expect(drawer.attributes('role')).toBe('dialog')
    expect(document.getElementById(drawer.attributes('aria-labelledby')!)?.textContent).toBe('Tools')
  })

  it('hands focus back to Open tools menu when Close tools menu closes it', async () => {
    const wrapper = await mountLayout(DefaultLayout, EN)
    const trigger = await openWithKeyboard(wrapper, 'Open tools menu')

    ;(wrapper.get('button[aria-label="Close tools menu"]').element as HTMLButtonElement).click()
    await settle()

    expect(focused()).toBe(trigger)
  })
})
