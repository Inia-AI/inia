import { describe, it, expect, afterEach, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { useFactTooltip } from './useFactTooltip'
import api from '@/api'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import { passthrough } from '@/test/helpers/rekaStubs'
import SourceChainPanel from '@/components/organisms/SourceChainPanel/SourceChainPanel.vue'
import { setAuthToken, clearAuthToken } from '@/test/helpers/jwt'
import { DRAWN_FROM_VISIBLE_COUNT } from '@/utils/citationUtils'
import { SOURCE_CHAIN_EVENT, type SourceChainEventDetail } from '@/utils/sourceChainEvents'
import { PAGE_PREVIEW_EVENT, type PagePreviewEventDetail } from '@/utils/pagePreviewEvents'
import type { BlockCitationSource, BlockCitedSources, Citation, UrlCitationSource } from '@/api/workflows/schema'

const MATCHING_URL = 'https://example.org/people/alex-rivera'
const LIST_LENGTH = 14

const url = (href: string, citedText = 'excerpt'): UrlCitationSource => ({ type: 'Url', url: href, citedText })

const blockCitation = (overrides: Partial<BlockCitationSource> = {}): Citation => ({
  id: 'c0',
  sources: [
    {
      type: 'Block',
      blockId: 'block-synthesis',
      blockTitle: 'Key Principals',
      citedText: 'a paraphrase',
      upstreamSources: [],
      ...overrides,
    },
  ],
})

const listOfLinks = () => [
  url(MATCHING_URL),
  ...Array.from({ length: LIST_LENGTH - 1 }, (_, index) => url(`https://example.org/source/${index}`)),
]

let wrapper: VueWrapper | null = null

const listFor = (sources: UrlCitationSource[]): BlockCitedSources[] => [{ blockId: 'block-synthesis', sources }]

function mountParagraph(citations: Citation[], citedSourcesByBlock: BlockCitedSources[] = []) {
  const Paragraph = defineComponent({
    setup() {
      const scope = ref<HTMLElement | null>(null)
      useFactTooltip(scope)
      return () =>
        h('div', {
          ref: scope,
          'data-citations': JSON.stringify(citations),
          'data-cited-sources-by-block': citedSourcesByBlock.length > 0 ? JSON.stringify(citedSourcesByBlock) : undefined,
          innerHTML: 'Partner since <fact id="c0">May 2011</fact>.',
        })
    },
  })
  wrapper = mount(Paragraph, { attachTo: document.body })
  return wrapper.element.querySelector('fact') as HTMLElement
}

function openPopup(fact: HTMLElement): HTMLElement {
  fact.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  return document.getElementById('fact-tooltip-container') as HTMLElement
}

const visibleItems = (popup: HTMLElement) =>
  Array.from(popup.querySelectorAll<HTMLElement>('.fact-drawn-from-item')).filter(item => !item.hidden)

afterEach(() => {
  document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  wrapper?.unmount()
  wrapper = null
  clearAuthToken()
  vi.unstubAllGlobals()
  const popup = document.getElementById('fact-tooltip-container')
  if (popup) {
    Reflect.deleteProperty(popup, 'offsetHeight')
  }
})

describe('useFactTooltip block citations', () => {
  it('shows the marker of a block citation to a user who is not a super admin', () => {
    setAuthToken({ role: 'Analyst' })

    const fact = mountParagraph([blockCitation()])

    expect(fact.classList.contains('fact-empty')).toBe(false)
  })

  it('lists the source that states the fact first, then fills up to the visible count', () => {
    const popup = openPopup(mountParagraph(
      [blockCitation({ matchingSources: [url(MATCHING_URL, 'start date: 2011-05-01')] })],
      listFor(listOfLinks()),
    ))

    const items = visibleItems(popup)
    expect(items).toHaveLength(DRAWN_FROM_VISIBLE_COUNT)
    expect(items[0]?.classList.contains('is-match')).toBe(true)
    expect(items[0]?.querySelector('a')?.getAttribute('href')).toBe(MATCHING_URL)
    expect(popup.querySelector('.fact-show-all-btn')?.textContent).toContain(String(LIST_LENGTH))
  })

  it('opens the whole list in place when the reader asks for all of it', () => {
    const popup = openPopup(mountParagraph([blockCitation()], listFor(listOfLinks())))

    ;(popup.querySelector('.fact-show-all-btn') as HTMLElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))

    expect(visibleItems(popup)).toHaveLength(LIST_LENGTH)
    expect(popup.querySelector('.fact-show-all-btn')).toBeNull()
    expect(popup.style.visibility).toBe('visible')
  })

  it('keeps the whole list in place while the reader scrolls inside it, and follows a page scroll', () => {
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 0
    })
    const fact = mountParagraph([blockCitation()], listFor(listOfLinks()))
    placeMarkerAt(fact, 600)
    const popup = openPopup(fact)
    ;(popup.querySelector('.fact-show-all-btn') as HTMLElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))
    const topWhenOpened = popup.style.top
    placeMarkerAt(fact, 500)

    popup.querySelector('.fact-drawn-from-list')?.dispatchEvent(new Event('scroll'))

    expect(visibleItems(popup)).toHaveLength(LIST_LENGTH)
    expect(popup.style.top).toBe(topWhenOpened)

    document.dispatchEvent(new Event('scroll'))

    expect(popup.style.top).not.toBe(topWhenOpened)
  })

  it('keeps the block name and the trace button under the list', () => {
    const popup = openPopup(mountParagraph([blockCitation()], listFor([url(MATCHING_URL)])))

    const footer = popup.querySelector('.fact-drawn-from-footer')
    expect(footer?.textContent).toContain('Key Principals')
    expect(footer?.querySelector('.fact-trace-btn')).not.toBeNull()
  })

  it('names a block that comes without its title as prior analysis, never by its id', () => {
    const popup = openPopup(mountParagraph([blockCitation({ blockTitle: undefined })]))

    expect(popup.textContent).toContain('prior analysis')
    expect(popup.textContent).not.toContain('block-synthesis')
  })

  it('hands the paragraph\'s lists to the chain panel when the reader traces a block', () => {
    const lists = listFor([url(MATCHING_URL)])
    const popup = openPopup(mountParagraph([blockCitation()], lists))
    let detail: SourceChainEventDetail | null = null
    const listener = (event: Event) => { detail = (event as CustomEvent<SourceChainEventDetail>).detail }
    window.addEventListener(SOURCE_CHAIN_EVENT, listener)

    ;(popup.querySelector('.fact-trace-btn') as HTMLElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))

    window.removeEventListener(SOURCE_CHAIN_EVENT, listener)
    expect(detail!.blockSource.blockId).toBe('block-synthesis')
    expect(detail!.citedSourcesByBlock).toEqual(lists)
  })

  it('tells the chain panel which marker to return focus to, and the paragraph to fall back on', () => {
    const fact = mountParagraph([blockCitation()])
    const popup = openPopup(fact)
    let detail: SourceChainEventDetail | null = null
    const listener = (event: Event) => { detail = (event as CustomEvent<SourceChainEventDetail>).detail }
    window.addEventListener(SOURCE_CHAIN_EVENT, listener)

    ;(popup.querySelector('.fact-trace-btn') as HTMLElement).dispatchEvent(new MouseEvent('click', { bubbles: true }))

    window.removeEventListener(SOURCE_CHAIN_EVENT, listener)
    expect(detail!.opener).toBe(fact)
    expect(detail!.fallback).toBe(fact.closest('[data-citations]'))
  })

  it('renders a block citation from a report built before the lists existed as it did', () => {
    const popup = openPopup(mountParagraph([blockCitation()]))

    expect(popup.querySelector('.fact-drawn-from')).toBeNull()
    expect(popup.querySelector('.fact-block-source')?.textContent).toContain('Key Principals')
    expect(popup.querySelector('.fact-trace-btn')).not.toBeNull()
  })

  it('opens below the marker when the popup does not fit above it', () => {
    const fact = mountParagraph([blockCitation()])
    placeMarkerAt(fact, 100)
    setPopupHeight(300)

    const popup = openPopup(fact)

    expect(popup.style.transform).toBe('translate(-50%, 0)')
    expect(parseFloat(popup.style.top)).toBeGreaterThan(100)
  })

  it('caps the popup to the side with more room when it fits on neither side', () => {
    vi.stubGlobal('innerHeight', 768)
    const fact = mountParagraph([blockCitation()])
    placeMarkerAt(fact, 400)
    setPopupHeight(window.innerHeight)

    const popup = openPopup(fact)

    expect(popup.style.transform).toBe('translate(-50%, -100%)')
    expect(popup.style.maxHeight).toBe('390px')
  })

  it('opens above the marker when there is room for it', () => {
    const fact = mountParagraph([blockCitation()])
    placeMarkerAt(fact, 600)
    setPopupHeight(300)

    const popup = openPopup(fact)

    expect(popup.style.transform).toBe('translate(-50%, -100%)')
    expect(parseFloat(popup.style.top)).toBeLessThan(600)
  })
})

describe('useFactTooltip keyboard access', () => {
  const urlCitation = (): Citation => ({
    id: 'c0',
    sources: [url('https://example.org/first'), url('https://example.org/second')],
  })

  const press = (target: Element, key: string, init: KeyboardEventInit = {}) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
    target.dispatchEvent(event)
    return event
  }

  const popup = () => document.getElementById('fact-tooltip-container') as HTMLElement

  const openByKeyboard = () => {
    const fact = mountParagraph([urlCitation()])
    fact.focus()
    press(fact, 'Enter')
    return fact
  }

  it('keeps the markers keyboard controls after the paragraph leaves edit mode', async () => {
    const isEditing = ref(false)
    const EditableParagraph = defineComponent({
      setup() {
        const scope = ref<HTMLElement | null>(null)
        useFactTooltip(scope)
        return () =>
          isEditing.value
            ? h('textarea')
            : h('div', {
                ref: scope,
                'data-citations': JSON.stringify([urlCitation()]),
                innerHTML: 'Partner since <fact id="c0">May 2011</fact>.',
              })
      },
    })
    wrapper = mount(EditableParagraph, { attachTo: document.body })

    isEditing.value = true
    await nextTick()
    isEditing.value = false
    await nextTick()

    const fact = wrapper.element.querySelector('fact') as HTMLElement
    expect(fact.getAttribute('role')).toBe('button')
    expect(fact.getAttribute('tabindex')).toBe('0')
  })

  it('moves focus to the first revealed source when Show all removes itself', () => {
    const fact = mountParagraph([blockCitation()], listFor(listOfLinks()))
    fact.focus()
    press(fact, 'Enter')
    const showAll = popup().querySelector<HTMLButtonElement>('.fact-show-all-btn')!
    showAll.focus()

    showAll.click()

    const firstRevealed = popup().querySelectorAll<HTMLElement>('.fact-drawn-from-item')[DRAWN_FROM_VISIBLE_COUNT]!
    expect(popup().querySelector('.fact-show-all-btn')).toBeNull()
    expect(firstRevealed.contains(document.activeElement)).toBe(true)
  })

  it('shows a source excerpt while its link has keyboard focus, and hides it when focus leaves', () => {
    openByKeyboard()
    const excerpt = () => document.getElementById('fact-cited-text-tooltip') as HTMLElement
    const link = document.activeElement as HTMLElement

    expect(excerpt().style.visibility).toBe('visible')
    expect(excerpt().textContent).toBe('excerpt')
    expect(link.getAttribute('aria-describedby')).toBe('fact-cited-text-tooltip')

    link.blur()

    expect(excerpt().style.visibility).toBe('hidden')
    expect(link.getAttribute('aria-describedby')).toBeNull()
  })

  it('makes a cited marker a named button in the tab order', () => {
    const fact = mountParagraph([urlCitation()])

    expect(fact.getAttribute('role')).toBe('button')
    expect(fact.getAttribute('tabindex')).toBe('0')
    expect(fact.getAttribute('aria-label')).toBe('Show sources for May 2011')
    expect(fact.getAttribute('aria-haspopup')).toBe('dialog')
    expect(fact.getAttribute('aria-expanded')).toBe('false')

    fact.focus()
    expect(document.activeElement).toBe(fact)
  })

  it('leaves a marker with no sources out of the tab order', () => {
    const fact = mountParagraph([{ id: 'c0', sources: [] }])

    expect(fact.getAttribute('role')).toBeNull()
    expect(fact.getAttribute('tabindex')).toBeNull()
  })

  it.each(['Enter', ' '])('opens the sources on %j and moves focus to the first one', key => {
    const fact = mountParagraph([urlCitation()])
    fact.focus()

    const event = press(fact, key)

    expect(event.defaultPrevented).toBe(true)
    expect(popup().style.visibility).toBe('visible')
    expect(popup().getAttribute('role')).toBe('dialog')
    expect(fact.getAttribute('aria-expanded')).toBe('true')
    expect(document.activeElement).toBe(popup().querySelector('a'))
  })

  it('closes on Escape and hands focus back to the marker', () => {
    const fact = openByKeyboard()

    press(document.activeElement!, 'Escape')

    expect(popup().style.visibility).toBe('hidden')
    expect(fact.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(fact)
  })

  it('hands focus back to the marker when Tab leaves the last source, and lets Tab move on from it', () => {
    const fact = openByKeyboard()
    const links = popup().querySelectorAll('a')
    ;(links[links.length - 1] as HTMLElement).focus()

    const event = press(document.activeElement!, 'Tab')

    expect(popup().style.visibility).toBe('hidden')
    expect(document.activeElement).toBe(fact)
    expect(event.defaultPrevented).toBe(false)
  })

  it('keeps focus on the marker when Shift+Tab leaves the first source', () => {
    const fact = openByKeyboard()

    const event = press(document.activeElement!, 'Tab', { shiftKey: true })

    expect(popup().style.visibility).toBe('hidden')
    expect(document.activeElement).toBe(fact)
    expect(event.defaultPrevented).toBe(true)
  })

  describe('document page preview', () => {
    const documentCitation = (): Citation => ({
      id: 'c0',
      sources: [{ type: 'Document', pageId: 'page-81', pageNumber: 81, documentName: 'Annual Report.pdf', citedText: 'excerpt' }],
    })

    let host: VueWrapper | null = null

    const dialog = () =>
      Array.from(document.body.querySelectorAll<HTMLElement>('[role="dialog"]')).find(element =>
        element.querySelector('[data-page-preview]'),
      ) ?? null
    const closeButton = () => dialog()!.querySelector<HTMLButtonElement>('button')!

    const openPreviewByKeyboard = async () => {
      vi.spyOn(api.Pages.service, 'getPageImage').mockResolvedValue({
        blob: new Blob(['page text'], { type: 'text/plain' }),
        contentType: 'text/plain',
      })
      host = mount(SourceChainPanel, {
        attachTo: document.body,
        global: {
          plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
          stubs: { ScrollArea: passthrough('ScrollArea') },
        },
      })
      const fact = mountParagraph([documentCitation()])
      fact.focus()
      press(fact, 'Enter')
      const link = popup().querySelector('.fact-document-link') as HTMLButtonElement
      expect(document.activeElement).toBe(link)
      link.click()
      await flushPromises()
      return link
    }

    afterEach(() => {
      host?.unmount()
      host = null
      vi.restoreAllMocks()
    })

    const expectPageButton = (page: Element | null, label: string) => {
      expect(page?.tagName).toBe('BUTTON')
      expect(page?.getAttribute('type')).toBe('button')
      expect(page?.hasAttribute('href')).toBe(false)
      expect(page?.getAttribute('aria-haspopup')).toBe('dialog')
      expect(page?.textContent).toBe(label)
    }

    it('offers a cited page as a button that opens a dialog, not a link to the top of the report', () => {
      openPopup(mountParagraph([documentCitation()]))

      expectPageButton(popup().querySelector('.fact-document-link'), 'Annual Report.pdf - page 81')
    })

    it('offers a page from a report built before structured citations the same way', () => {
      const Paragraph = defineComponent({
        setup() {
          const scope = ref<HTMLElement | null>(null)
          useFactTooltip(scope)
          return () =>
            h('div', {
              ref: scope,
              innerHTML:
                'Revenue was <fact>$4.1m<fact_source type="document" label="Annual Report.pdf" page-id="page-81"></fact_source></fact>.',
            })
        },
      })
      wrapper = mount(Paragraph, { attachTo: document.body })

      openPopup(wrapper.element.querySelector('fact') as HTMLElement)

      expectPageButton(popup().querySelector('.fact-document-link'), 'Annual Report.pdf')
    })

    it('opens as a dialog named for the page and moves focus to its named close button', async () => {
      await openPreviewByKeyboard()

      const titleId = dialog()!.getAttribute('aria-labelledby')!
      expect(document.getElementById(titleId)!.textContent).toContain('Annual Report.pdf')
      expect(closeButton().getAttribute('type')).toBe('button')
      expect(closeButton().textContent!.trim()).toBe('Close')
      expect(dialog()!.contains(document.activeElement)).toBe(true)
    })

    it('asks for the page with the link to return focus to and the marker to fall back on', () => {
      const fact = mountParagraph([documentCitation()])
      openPopup(fact)
      let detail: PagePreviewEventDetail | null = null
      const listener = (event: Event) => { detail = (event as CustomEvent<PagePreviewEventDetail>).detail }
      window.addEventListener(PAGE_PREVIEW_EVENT, listener)

      const link = popup().querySelector('.fact-document-link') as HTMLButtonElement
      link.click()

      window.removeEventListener(PAGE_PREVIEW_EVENT, listener)
      expect(detail).toMatchObject({ pageId: 'page-81', opener: link, fallback: fact })
    })

    it('keeps Tab and Shift+Tab inside the preview', async () => {
      await openPreviewByKeyboard()
      closeButton().focus()

      const forward = press(closeButton(), 'Tab')
      expect(forward.defaultPrevented).toBe(true)
      expect(dialog()!.contains(document.activeElement)).toBe(true)

      const backward = press(document.activeElement!, 'Tab', { shiftKey: true })
      expect(backward.defaultPrevented).toBe(true)
      expect(dialog()!.contains(document.activeElement)).toBe(true)
    })

    it('keeps the sources open when the control clicked is gone by the time the page hears the click, as the preview\'s close is', async () => {
      await openPreviewByKeyboard()
      press(document.activeElement!, 'Escape')
      await flushPromises()
      const layer = document.createElement('div')
      const control = document.createElement('button')
      layer.appendChild(control)
      document.body.appendChild(layer)
      control.addEventListener('click', () => layer.remove())

      control.click()

      expect(popup().style.visibility).toBe('visible')
    })

    it.each([
      ['Escape', () => press(document.activeElement!, 'Escape')],
      ['the close button', () => closeButton().click()],
    ])('closes on %s, keeps the sources open and hands focus back to the page link', async (_, close) => {
      const link = await openPreviewByKeyboard()

      close()
      await flushPromises()

      expect(dialog()).toBeNull()
      expect(document.activeElement).toBe(link)
      expect(popup().style.visibility).toBe('visible')
    })
  })
})

describe('useFactTooltip cited links', () => {
  const popup = () => document.getElementById('fact-tooltip-container') as HTMLElement

  it('links a web address and shows any other cited address as plain text, never as a link to the top of the report', () => {
    const fact = mountParagraph([{ id: 'c0', sources: [url('https://example.org/profile'), url('javascript:alert(1)')] }])

    openPopup(fact)

    expect(Array.from(popup().querySelectorAll('a')).map(link => link.getAttribute('href'))).toEqual(['https://example.org/profile'])
    expect(popup().textContent).toContain('javascript:alert(1)')
  })

  it('does the same for a web source in a report built before structured citations', () => {
    const Paragraph = defineComponent({
      setup() {
        const scope = ref<HTMLElement | null>(null)
        useFactTooltip(scope)
        return () =>
          h('div', {
            ref: scope,
            innerHTML: 'Founded <fact>2011<fact_source type="web" label="intranet/profile"></fact_source></fact>.',
          })
      },
    })
    wrapper = mount(Paragraph, { attachTo: document.body })

    openPopup(wrapper.element.querySelector('fact') as HTMLElement)

    expect(popup().querySelector('a')).toBeNull()
    expect(popup().textContent).toContain('intranet/profile')
  })
})

function placeMarkerAt(fact: HTMLElement, top: number) {
  fact.getBoundingClientRect = () => ({ top, bottom: top + 20, left: 400, right: 480, width: 80, height: 20, x: 400, y: top, toJSON: () => ({}) })
}

function setPopupHeight(height: number) {
  const popup = document.getElementById('fact-tooltip-container') ?? (() => {
    const created = document.createElement('div')
    created.id = 'fact-tooltip-container'
    document.body.appendChild(created)
    return created
  })()
  Object.defineProperty(popup, 'offsetHeight', { configurable: true, get: () => height })
}

