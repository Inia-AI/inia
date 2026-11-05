/**
 * Wiring tests for the document page: its header and details, the page the address names, and
 * the text download. The page reader has its own tests.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'

const { documentOverrides, loadFailure, route, replaceMock, downloadTextMock, downloadBlobMock, toastMock } = vi.hoisted(() => ({
  documentOverrides: { value: {} as Record<string, unknown> },
  loadFailure: { status: null as number | null },
  route: { params: { id: 'doc-1' }, query: {} as Record<string, string> },
  replaceMock: vi.fn(),
  downloadTextMock: vi.fn(),
  downloadBlobMock: vi.fn(),
  toastMock: vi.fn(),
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Documents: {
        useGetDocumentById: () => ({
          data: vueRef(loadFailure.status ? undefined : {
            id: 'doc-1',
            name: 'annual-report',
            extension: '.pdf',
            mediaType: 'ApplicationPdf',
            sizeBytes: 1024,
            createdAt: '2026-10-06T10:00:00Z',
            pageCount: 3,
            sources: [],
            documentCategoryId: 'cat-1',
            userId: 'user-1',
            class: 'Annual Report',
            ...documentOverrides.value,
          }),
          isLoading: vueRef(false),
          isError: vueRef(loadFailure.status !== null),
          error: vueRef(loadFailure.status ? { response: { status: loadFailure.status } } : null),
        }),
        useDownloadDocumentText: () => ({ mutateAsync: downloadTextMock, isPending: vueRef(false) }),
      },
      DocumentCategories: { useGetDocumentCategories: () => ({ data: vueRef([{ id: 'cat-1', name: 'Filings' }]) }) },
      Users: { useGetUsers: () => ({ data: vueRef({ value: [{ id: 'user-1', email: 'analyst@example.test' }] }) }) },
      Files: { useGetFile: () => ({ mutateAsync: vi.fn() }) },
    },
  }
})

vi.mock('@/lib/downloadFile', async importOriginal => ({
  ...(await importOriginal<typeof import('@/lib/downloadFile')>()),
  downloadBlob: downloadBlobMock,
}))

vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: toastMock }) }))

vi.mock('vue-router', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => route,
  useRouter: () => ({ back: vi.fn(), push: vi.fn(), replace: replaceMock }),
}))

import DocumentDetails from './DocumentDetails.vue'

const ReaderStub = {
  name: 'DocumentPageReader',
  props: { documentId: String, pageCount: Number, pageNumber: Number },
  emits: ['update:pageNumber'],
  template: '<div data-test="reader">{{ documentId }}|{{ pageCount }}|{{ pageNumber }}</div>',
}

const mountView = async () => {
  const wrapper = mount(DocumentDetails, {
    global: { stubs: { DocumentPageReader: ReaderStub, FilePreviewDialog: true } },
  })
  await flushPromises()
  return wrapper
}

describe('DocumentDetails.vue', () => {
  beforeEach(() => {
    documentOverrides.value = {}
    loadFailure.status = null
    route.query = reactive({})
    replaceMock.mockReset()
    downloadTextMock.mockReset().mockResolvedValue({ blob: new Blob(['<page1>']), filename: 'annual-report.txt' })
    downloadBlobMock.mockReset()
    toastMock.mockReset()
  })

  it.each([
    [404, 'page.documents.detail.notFoundTitle'],
    [500, 'page.documents.detail.loadErrorTitle'],
  ])('heads the page with what went wrong when the platform answers %i', async (status, title) => {
    loadFailure.status = status

    const wrapper = await mountView()

    expect(wrapper.find('h1').text()).toBe(title)
  })

  it('names the category and the uploader under the title, and keeps the other facts behind Details', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('.page-subtitle').text()).toContain('Filings')
    expect(wrapper.get('.page-subtitle').text()).toContain('page.documents.detail.uploadedBy:{"user":"analyst@example.test"}')
    expect(wrapper.text()).not.toContain('Annual Report')
    expect(wrapper.get('[data-testid="details-toggle"]').attributes('aria-controls')).toBeUndefined()

    await wrapper.get('[data-testid="details-toggle"]').trigger('click')

    const toggle = wrapper.get('[data-testid="details-toggle"]')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(wrapper.get(`#${toggle.attributes('aria-controls')}`).text()).toContain('Annual Report')
  })

  it('opens the page the address names, and the first page when it names none', async () => {
    route.query = reactive({ page: '2' })
    const named = await mountView()
    route.query = reactive({})
    const unnamed = await mountView()

    expect(named.get('[data-test="reader"]').text()).toBe('doc-1|3|2')
    expect(unnamed.get('[data-test="reader"]').text()).toBe('doc-1|3|1')
  })

  it('writes the page the reader moves to into the address', async () => {
    route.query = reactive({ page: '2', other: 'kept' })
    const wrapper = await mountView()

    wrapper.getComponent(ReaderStub).vm.$emit('update:pageNumber', 3)

    expect(replaceMock).toHaveBeenCalledWith({ query: { page: '3', other: 'kept' } })
  })

  it('says a document with no pages has none, instead of opening a reader', async () => {
    documentOverrides.value = { pageCount: 0 }
    const wrapper = await mountView()

    expect(wrapper.find('[data-test="reader"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('page.documents.detail.pages.empty')
  })

  it('downloads the text of every page under the name the server gives', async () => {
    const wrapper = await mountView()

    await wrapper.get('[data-testid="download-text"]').trigger('click')
    await flushPromises()

    expect(downloadTextMock).toHaveBeenCalledWith('doc-1')
    expect(downloadBlobMock).toHaveBeenCalledWith(expect.any(Blob), 'annual-report.txt')
  })

  it('says why the platform refused the text download', async () => {
    downloadTextMock.mockRejectedValue({
      response: { status: 403, data: new Blob([JSON.stringify({ title: 'You cannot read this document.' })], { type: 'application/json' }) },
    })
    const wrapper = await mountView()

    await wrapper.get('[data-testid="download-text"]').trigger('click')
    await flushPromises()

    expect(downloadBlobMock).not.toHaveBeenCalled()
    expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({ description: 'You cannot read this document.' }))
  })
})
