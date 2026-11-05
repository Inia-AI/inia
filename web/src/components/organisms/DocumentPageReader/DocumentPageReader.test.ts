import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import type { Ref } from 'vue'
import { EN } from '@/assets/locales/en'
import type { PageDetail, PageSummary } from '@/api/documents/schema'

const { pagesParamsSeen, getPageImageMock, toastMock } = vi.hoisted(() => ({
  pagesParamsSeen: { value: null as Ref<Record<string, unknown>> | null },
  getPageImageMock: vi.fn(),
  toastMock: vi.fn(),
}))

const summary = (overrides: Partial<PageSummary>): PageSummary => ({
  id: 'p1',
  documentId: 'doc-1',
  pageNumber: 1,
  filePageNumber: 1,
  textPreview: null,
  overview: null,
  location: 'FullPage',
  status: 'OCRSuccessful',
  hasEmbedding: false,
  embeddingModel: null,
  sourceUrl: null,
  ...overrides,
})

const PAGES: PageSummary[] = [
  summary({ id: 'p1', pageNumber: 1, overview: 'The income statement', hasEmbedding: true, embeddingModel: 'titan-embed-v2' }),
  summary({ id: 'p2', pageNumber: 2, filePageNumber: 2, location: 'LeftSide', status: 'OCRNeedsReview', overview: 'A signed declaration' }),
  summary({ id: 'p3', pageNumber: 3, filePageNumber: 2, location: 'RightSide', status: 'ProcessedSuccessfully' }),
]

const TABLE_TEXT = '| Item | 2025 |\n|---|---|\n| Cash | 1,204 |'

const DETAILS: Record<number, PageDetail> = {
  1: { ...PAGES[0]!, text: TABLE_TEXT, readableText: TABLE_TEXT, embeddingDimensions: 1024, sectionTitles: 'Balance sheet, Notes' },
  2: { ...PAGES[1]!, text: null, readableText: 'A signed declaration', embeddingDimensions: null, sectionTitles: null },
  3: { ...PAGES[2]!, text: 'Notes to the accounts', readableText: 'Notes to the accounts', embeddingDimensions: null, sectionTitles: null },
}

vi.mock('@/api', async () => {
  const { ref: vueRef, computed: vueComputed } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Documents: {
        useGetDocumentPages: (_id: unknown, params: Ref<Record<string, unknown>>) => {
          pagesParamsSeen.value = params
          return {
            data: vueComputed(() => (params.value.term ? { value: [], count: 0 } : { value: PAGES, count: PAGES.length })),
          }
        },
        useGetDocumentPage: (_id: unknown, pageNumber: Ref<number | null>) => ({
          data: vueComputed(() => (pageNumber.value ? DETAILS[pageNumber.value] : undefined)),
          isLoading: vueRef(false),
          isError: vueRef(false),
        }),
      },
      Pages: { service: { getPageImage: getPageImageMock } },
    },
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: toastMock }) }))

import DocumentPageReader from './DocumentPageReader.vue'

const READER = EN.page.documents.detail.reader
const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })
const t = i18n.global.t

const mountReader = async (pageNumber = 1) => {
  const wrapper: VueWrapper = mount(DocumentPageReader, {
    props: {
      documentId: 'doc-1',
      pageCount: PAGES.length,
      pageNumber,
      'onUpdate:pageNumber': (next: number) => wrapper.setProps({ pageNumber: next }),
    },
    global: { plugins: [i18n], stubs: { PagePreviewDialog: true } },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

const buttonNamed = (wrapper: VueWrapper, name: string) =>
  wrapper.findAll('button').find(button => button.text() === name || button.attributes('aria-label') === name)!

describe('DocumentPageReader', () => {
  let mounted: VueWrapper | null = null
  const writeText = vi.fn()

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    getPageImageMock.mockReset().mockResolvedValue({ blob: new Blob(['png']), contentType: 'image/png' })
    toastMock.mockReset()
    writeText.mockReset().mockResolvedValue(undefined)
    Object.defineProperty(URL, 'createObjectURL', { value: vi.fn(() => 'blob:page-image'), configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
  })

  afterEach(() => {
    mounted?.unmount()
    mounted = null
    vi.useRealTimers()
  })

  it('shows the original page beside its text, exactly as it is stored', async () => {
    mounted = await mountReader(1)

    expect(mounted.text()).toContain(t('page.documents.detail.reader.pageOf', { number: 1, count: 3 }))
    expect(getPageImageMock).toHaveBeenCalledWith('p1')
    expect(mounted.get('[data-testid="page-original"] img').attributes('src')).toBe('blob:page-image')
    expect(mounted.get('[data-testid="page-text"]').text()).toBe(TABLE_TEXT)
  })

  it('draws the tables of the text when asked for it formatted', async () => {
    mounted = await mountReader(1)

    await buttonNamed(mounted, READER.formatted).trigger('click')

    const formatted = mounted.get('[data-testid="page-text-formatted"]')
    expect(formatted.find('table').exists()).toBe(true)
    expect(formatted.text()).toContain('1,204')
  })

  it('copies the text exactly as it is stored', async () => {
    mounted = await mountReader(1)

    await mounted.get('[data-testid="copy-text"]').trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith(TABLE_TEXT)
    expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({ description: READER.copied }))
  })

  it('reads a page OCR judged unreliable through its overview, and says so', async () => {
    mounted = await mountReader(2)

    expect(mounted.text()).toContain(READER.needsReview)
    expect(mounted.find('[data-testid="page-text"]').exists()).toBe(false)
    expect(mounted.get('[data-testid="page-overview"]').text()).toBe('A signed declaration')
  })

  it('gives the text the whole width when a file type has no page image, and says why', async () => {
    getPageImageMock.mockResolvedValue({ blob: new Blob(['Notes to the accounts']), contentType: 'text/plain' })
    mounted = await mountReader(3)

    expect(mounted.get('[data-testid="no-page-image"]').text()).toBe(READER.noImage)
    expect(mounted.find('[data-testid="page-original"]').exists()).toBe(false)
    expect(mounted.get('[data-testid="page-text"]').text()).toBe('Notes to the accounts')
  })

  it('shows the overview, the section titles and the embedding of the page', async () => {
    mounted = await mountReader(1)

    expect(mounted.get('[data-testid="page-overview"]').text()).toBe('The income statement')
    expect(mounted.text()).toContain('Balance sheet, Notes')
    expect(mounted.text()).toContain(t('page.documents.detail.reader.embeddingValue', { model: 'titan-embed-v2', dimensions: 1024 }))
  })

  it('steps to the next and the previous page, and stops at the ends', async () => {
    mounted = await mountReader(1)

    expect(buttonNamed(mounted, READER.previous).attributes('aria-disabled')).toBe('true')
    await buttonNamed(mounted, READER.next).trigger('click')
    await buttonNamed(mounted, READER.next).trigger('click')
    await buttonNamed(mounted, READER.next).trigger('click')

    expect(mounted.emitted('update:pageNumber')).toEqual([[2], [3]])
    expect(buttonNamed(mounted, READER.next).attributes('aria-disabled')).toBe('true')
  })

  it.each([
    { name: READER.next, from: 2, to: 3 },
    { name: READER.previous, from: 2, to: 1 },
  ])('keeps focus on $name when the step reaches an end, and announces the new page', async ({ name, from, to }) => {
    mounted = await mountReader(from)
    const step = buttonNamed(mounted, name).element as HTMLButtonElement
    step.focus()

    step.click()
    await flushPromises()

    expect(step.isConnected).toBe(true)
    expect(step.disabled).toBe(false)
    expect(document.activeElement).toBe(step)
    const announcement = mounted.get('[aria-live="polite"]')
    expect(announcement.attributes('aria-atomic')).toBe('true')
    expect(announcement.text()).toBe(t('page.documents.detail.reader.pageOf', { number: to, count: 3 }))
  })

  it('steps with the arrow keys, but not while the search box has focus', async () => {
    mounted = await mountReader(2)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }))
    await flushPromises()
    mounted.get('[data-testid="page-search"]').element.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    await flushPromises()

    expect(mounted.emitted('update:pageNumber')).toEqual([[1]])
  })

  it('lists the half of the file each page came from, and how it was read', async () => {
    mounted = await mountReader(1)

    const second = mounted.findAll('[data-testid="page-list-item"]')[1]!
    expect(second.text()).toContain(READER.location.LeftSide)
    expect(second.text()).toContain(READER.conversion.OCRNeedsReview)
  })

  it('scrolls the list to the selected page', async () => {
    const scrollIntoView = vi.fn()
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true })
    mounted = await mountReader(3)

    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'nearest' })
    expect(scrollIntoView.mock.contexts.at(-1)).toBe(mounted.findAll('[data-testid="page-list-item"]')[2]!.element)
  })

  it('opens a page from the list, and searches the text on the server', async () => {
    mounted = await mountReader(1)

    await mounted.findAll('[data-testid="page-list-item"]')[2]!.trigger('click')
    await mounted.get('[data-testid="page-search"]').setValue('covenant')
    await vi.advanceTimersByTimeAsync(400)

    expect(mounted.emitted('update:pageNumber')).toEqual([[3]])
    expect(pagesParamsSeen.value!.value).toMatchObject({ term: 'covenant', $skip: 0 })
    expect(mounted.text()).toContain(t('page.documents.detail.pages.noMatch', { term: 'covenant' }))
  })
})
