import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import api from '@/api'
import type { BlockCitationSource, DocumentCitationSource, UrlCitationSource } from '@/api/workflows/schema'
import { passthrough } from '@/test/helpers/rekaStubs'
import { SOURCE_CHAIN_EVENT, type SourceChainEventDetail } from '@/utils/sourceChainEvents'
import { PAGE_PREVIEW_EVENT, type PagePreviewEventDetail } from '@/utils/pagePreviewEvents'
import SourceChainPanel from './SourceChainPanel.vue'
import PagePreviewDialog from '@/components/organisms/PagePreviewDialog/PagePreviewDialog.vue'

const BLOCK_ID = 'block-fund-facts'

const blockSource: BlockCitationSource = {
  type: 'Block',
  blockId: BLOCK_ID,
  blockTitle: 'Fund facts',
  citedText: 'Excess of assets under management over net asset value: $6,162,000',
  upstreamSources: [],
}

const link = (url: string): UrlCitationSource => ({ type: 'Url', url, citedText: `About ${url}` })

let wrapper: VueWrapper | null = null

const mountPanel = ({ teleport = false } = {}) =>
  mount(SourceChainPanel, {
    attachTo: document.body,
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
      stubs: { teleport: !teleport, ScrollArea: passthrough('ScrollArea') },
    },
  })

async function openWith(
  sources: (UrlCitationSource | DocumentCitationSource)[],
  { opener, fallback, teleport }: { opener?: HTMLElement; fallback?: HTMLElement; teleport?: boolean } = {},
) {
  wrapper = mountPanel({ teleport })
  window.dispatchEvent(new CustomEvent<SourceChainEventDetail>(SOURCE_CHAIN_EVENT, {
    detail: { blockSource, citedSourcesByBlock: [{ blockId: BLOCK_ID, sources }], opener, fallback },
  }))
  await nextTick()
  return wrapper
}

const rows = () => wrapper!.findAll('.source-chain-source-btn')

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  vi.restoreAllMocks()
})

describe('SourceChainPanel drawn from list', () => {
  it('lists the pages and links a block drew on, five first, then all of them on request', async () => {
    await openWith(Array.from({ length: 7 }, (_, index) => link(`https://example.org/northwind/${index}`)))

    expect(wrapper!.text()).toContain('Drawn from (7)')
    expect(rows()).toHaveLength(5)

    await wrapper!.find('.source-chain-show-all').trigger('click')

    expect(rows()).toHaveLength(7)
    expect(wrapper!.find('.source-chain-show-all').exists()).toBe(false)
  })

  it('opens a web link in a new tab', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    await openWith([link('https://example.org/northwind/profile')])

    await rows()[0]!.trigger('click')

    expect(open).toHaveBeenCalledWith('https://example.org/northwind/profile', '_blank', 'noopener,noreferrer')
  })

  it('does not open or offer a cited link that is not a web address', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    await openWith([link('javascript:alert(1)')])

    await rows()[0]!.trigger('click')

    expect(open).not.toHaveBeenCalled()
    expect(rows()[0]!.classes()).not.toContain('clickable')
  })

  it('shows the dead end only when the block has neither upstream sources nor a list', async () => {
    await openWith([])

    expect(wrapper!.find('.source-chain-empty').exists()).toBe(true)
  })
})

describe('SourceChainPanel header buttons', () => {
  const nameOf = (selector: string) => wrapper!.get(selector).attributes('aria-label')

  it('names the back button apart from the close button when the chain holds one step', async () => {
    await openWith([link('https://example.org/northwind/profile')])

    expect(nameOf('.source-chain-back-btn')).toBe('Go back')
    expect(nameOf('.source-chain-close-btn')).toBe('Close panel')
  })

  it('keeps the two names apart once the chain is traced a step deeper, and the back button still steps back', async () => {
    wrapper = mountPanel()
    const upstream: BlockCitationSource = { ...blockSource, blockId: 'block-peers', blockTitle: 'Peer set', upstreamSources: [] }
    window.dispatchEvent(new CustomEvent<SourceChainEventDetail>(SOURCE_CHAIN_EVENT, {
      detail: { blockSource: { ...blockSource, upstreamSources: [upstream] }, citedSourcesByBlock: [] },
    }))
    await nextTick()
    await rows()[0]!.trigger('click')

    expect(wrapper.findAll('.source-chain-breadcrumb')).toHaveLength(2)
    expect(nameOf('.source-chain-back-btn')).toBe('Go back')
    expect(nameOf('.source-chain-close-btn')).toBe('Close panel')

    await wrapper.get('.source-chain-back-btn').trigger('click')

    expect(wrapper.findAll('.source-chain-breadcrumb')).toHaveLength(1)
  })
})

describe('SourceChainPanel keyboard access', () => {
  const page = (pageNumber: number): DocumentCitationSource => ({
    type: 'Document',
    pageId: `page-${pageNumber}`,
    pageNumber,
    documentName: 'Lender Presentation.pdf',
    citedText: 'excerpt',
  })

  const pressEscape = () =>
    document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  const previewDialog = () =>
    Array.from(document.body.querySelectorAll<HTMLElement>('[role="dialog"]')).find(dialog =>
      dialog.querySelector('[data-page-preview]'),
    ) ?? null

  const stubPage = () =>
    vi.spyOn(api.Pages.service, 'getPageImage').mockResolvedValue({
      blob: new Blob(['page text'], { type: 'text/plain' }),
      contentType: 'text/plain',
    })

  it('moves focus into the panel when it opens, and back to the marker that opened it when it closes', async () => {
    const marker = document.createElement('button')
    document.body.appendChild(marker)
    marker.focus()

    await openWith([page(27)], { opener: marker })

    expect(wrapper!.find('.source-chain-panel').element.contains(document.activeElement)).toBe(true)

    pressEscape()
    await nextTick()

    expect(wrapper!.find('.source-chain-panel').exists()).toBe(false)
    expect(document.activeElement).toBe(marker)
    marker.remove()
  })

  it('keeps focus inside the panel while it is open', async () => {
    const outside = document.createElement('button')
    document.body.appendChild(outside)
    await openWith([page(27)])
    const panel = wrapper!.find('.source-chain-panel').element

    outside.focus()
    await nextTick()

    expect(panel.contains(document.activeElement)).toBe(true)
    outside.remove()
  })

  it('falls back to the paragraph when the marker that opened it is gone', async () => {
    const paragraph = document.createElement('div')
    const firstMarker = document.createElement('button')
    const marker = document.createElement('button')
    paragraph.append(firstMarker, marker)
    document.body.appendChild(paragraph)
    marker.focus()
    await openWith([page(27)], { opener: marker, fallback: paragraph })
    marker.remove()

    pressEscape()
    await nextTick()

    expect(document.activeElement).toBe(firstMarker)
    paragraph.remove()
  })

  it('gives the page preview the panel to fall back on when the row that opened it is gone', async () => {
    stubPage()
    await openWith([page(27)], { teleport: true })

    document.body.querySelector<HTMLButtonElement>('.source-chain-source-btn')!.click()
    await flushPromises()

    expect(wrapper!.findComponent(PagePreviewDialog).props('fallbackFocus')).toBe(
      document.body.querySelector('.source-chain-panel'),
    )
  })

  it('opens a page from the panel as a named dialog with focus inside, and Escape closes only the page', async () => {
    stubPage()
    await openWith([page(27)], { teleport: true })
    const row = document.body.querySelector<HTMLButtonElement>('.source-chain-source-btn')!
    row.focus()

    row.click()
    await flushPromises()

    const dialog = previewDialog()!
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)!.textContent).toContain(
      'Lender Presentation.pdf - page 27',
    )
    const close = dialog.querySelector<HTMLButtonElement>('button')!
    expect(close.getAttribute('type')).toBe('button')
    expect(close.textContent!.trim()).toBe('Close')
    expect(dialog.contains(document.activeElement)).toBe(true)

    pressEscape()
    await flushPromises()

    expect(previewDialog()).toBeNull()
    expect(document.body.querySelector('.source-chain-panel')).not.toBeNull()
    expect(document.activeElement).toBe(row)
  })

  it('opens the page a citation popup asks for, and hands focus back to its link', async () => {
    stubPage()
    wrapper = mountPanel({ teleport: true })
    const link = document.createElement('a')
    link.href = '#'
    document.body.appendChild(link)
    link.focus()

    window.dispatchEvent(new CustomEvent<PagePreviewEventDetail>(PAGE_PREVIEW_EVENT, {
      detail: { pageId: 'page-81', title: 'Annual Report.pdf - page 81', opener: link },
    }))
    await flushPromises()

    expect(previewDialog()!.textContent).toContain('Annual Report.pdf - page 81')
    expect(previewDialog()!.contains(document.activeElement)).toBe(true)

    previewDialog()!.querySelector<HTMLButtonElement>('button')!.click()
    await flushPromises()

    expect(previewDialog()).toBeNull()
    expect(document.activeElement).toBe(link)
    link.remove()
  })
})
