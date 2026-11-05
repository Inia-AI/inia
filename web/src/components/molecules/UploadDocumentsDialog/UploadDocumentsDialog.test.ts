/**
 * The upload dialog stores each picked file, files it under the category chosen for it, and asks
 * the platform to read it into pages. Each file shows its own state until it is read or fails.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { defineComponent, h, type Ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { dialogStubs } from '@/test/helpers/rekaStubs'

const { uploadMock, processMock, documentState } = vi.hoisted(() => ({
  uploadMock: vi.fn(),
  processMock: vi.fn(),
  documentState: vi.fn(),
}))

vi.mock('@/api', async () => {
  const { ref: vueRef, computed: vueComputed } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Files: { useUploadDocument: () => ({ mutateAsync: uploadMock }) },
      Documents: {
        useProcessDocument: () => ({ mutateAsync: processMock }),
        useWatchDocumentProcessing: (id: Ref<string>) => ({
          data: vueComputed(() => ({ id: id.value, ...documentState(id.value) })),
          error: vueRef(null),
        }),
      },
      DocumentCategories: {
        useGetDocumentCategories: () => ({
          data: vueRef([
            { id: 'company-report', name: 'Company Report', displayOrder: 1 },
            { id: 'presentation', name: 'Presentation', displayOrder: 2 },
          ]),
        }),
      },
    },
  }
})

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

import UploadDocumentsDialog from './UploadDocumentsDialog.vue'

const SelectStub = defineComponent({
  name: 'IniaSelect',
  props: { modelValue: String, options: Array },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h(
        'select',
        {
          'data-testid': 'category',
          value: props.modelValue,
          onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLSelectElement).value),
        },
        (props.options as { label: string; value: string }[]).map(option => h('option', { value: option.value }, option.label)),
      )
  },
})

const mountDialog = () =>
  mount(UploadDocumentsDialog, {
    props: { open: true },
    global: {
      plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }]],
      stubs: { ...dialogStubs, IniaSelect: SelectStub, IniaIcon: true },
    },
  })

const pick = async (wrapper: ReturnType<typeof mountDialog>, files: File[]) => {
  const input = wrapper.find('[data-testid="upload-input"]')
  Object.defineProperty(input.element, 'files', { value: files, configurable: true })
  await input.trigger('change')
  await flushPromises()
}

const rowStates = (wrapper: ReturnType<typeof mountDialog>) =>
  wrapper.findAll('[data-testid="upload-row"]').map(row => [
    row.find('[data-testid="file-name"]').text(),
    row.find('[data-testid="file-state"]').text(),
  ])

describe('UploadDocumentsDialog', () => {
  beforeEach(() => {
    let next = 0
    uploadMock.mockReset().mockImplementation(async (variables: { formData: FormData }) => ({
      id: `doc-${++next}`,
      filename: 'f',
      mediaType: 'x',
      created: true,
      producesPages: !(variables.formData.get('file') as File).name.endsWith('.eml'),
    }))
    processMock.mockReset().mockImplementation(async (id: string) => ({ id, processingState: 'Queued' }))
    documentState.mockReset().mockReturnValue({ processingState: 'Processed', processingError: null })
  })

  it('lists each picked file with no category', async () => {
    const wrapper = mountDialog()
    await pick(wrapper, [new File(['a'], 'annual-report.pdf'), new File(['b'], 'deck.pptx')])

    expect(rowStates(wrapper)).toEqual([
      ['annual-report.pdf', 'page.documents.upload.state.pending'],
      ['deck.pptx', 'page.documents.upload.state.pending'],
    ])
    expect(wrapper.findAll('[data-testid="category"]').map(select => (select.element as HTMLSelectElement).value)).toEqual(['', ''])
  })

  it('stores each file under its category, and has it read into pages', async () => {
    const wrapper = mountDialog()
    await pick(wrapper, [new File(['a'], 'annual-report.pdf'), new File(['b'], 'term-sheet.docx')])
    await wrapper.findAll('[data-testid="category"]')[0].setValue('company-report')

    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()

    const categories = uploadMock.mock.calls.map(([variables]) => (variables as { formData: FormData }).formData.get('documentCategoryId'))
    expect(categories).toEqual(['company-report', null])
    expect(processMock.mock.calls.map(call => call[0])).toEqual(['doc-1', 'doc-2'])
    expect(rowStates(wrapper)).toEqual([
      ['annual-report.pdf', 'page.documents.upload.state.processed'],
      ['term-sheet.docx', 'page.documents.upload.state.processed'],
    ])
  })

  it('queues every file before it waits for any of them to be read', async () => {
    documentState.mockImplementation((id: string) => ({ processingState: id === 'doc-1' ? 'Processing' : 'Processed', processingError: null }))
    const wrapper = mountDialog()
    await pick(wrapper, [new File(['a'], 'annual-report.pdf'), new File(['b'], 'deck.pptx')])

    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()

    expect(processMock.mock.calls.map(call => call[0])).toEqual(['doc-1', 'doc-2'])
    expect(rowStates(wrapper)).toEqual([
      ['annual-report.pdf', 'page.documents.upload.state.processing'],
      ['deck.pptx', 'page.documents.upload.state.processed'],
    ])
    wrapper.unmount()
  })

  it('shows why a file failed and still finishes the others', async () => {
    processMock.mockImplementation(async (id: string) => {
      if (id === 'doc-1') {
        throw { isAxiosError: true, response: { status: 409, data: { title: 'The document is already being processed.' } } }
      }
      return { id, processingState: 'Queued' }
    })
    documentState.mockReturnValue({ processingState: 'Failed', processingError: 'The file is damaged.' })
    const wrapper = mountDialog()
    await pick(wrapper, [new File(['a'], 'scan.pdf'), new File(['b'], 'broken.pdf')])

    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()

    expect(rowStates(wrapper)).toEqual([
      ['scan.pdf', 'page.documents.upload.state.failed'],
      ['broken.pdf', 'page.documents.upload.state.failed'],
    ])
    const errors = wrapper.findAll('[data-testid="file-error"]').map(error => error.text())
    expect(errors).toEqual(['The document is already being processed.', 'The file is damaged.'])
  })

  it('stores an email without asking to read it, because the platform reads no email into pages', async () => {
    const wrapper = mountDialog()
    await pick(wrapper, [new File(['a'], 'newsletter.eml')])

    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()

    expect(rowStates(wrapper)).toEqual([['newsletter.eml', 'page.documents.upload.state.stored']])
    expect(processMock).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="file-error"]').exists()).toBe(false)
  })

  it('starts nothing until a file is picked, and leaves finished files alone on a second run', async () => {
    const wrapper = mountDialog()
    expect(wrapper.find('[data-testid="start-upload"]').attributes('disabled')).toBeDefined()

    await pick(wrapper, [new File(['a'], 'annual-report.pdf')])
    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()
    await pick(wrapper, [new File(['b'], 'deck.pptx')])
    await wrapper.find('[data-testid="start-upload"]').trigger('click')
    await flushPromises()

    expect(uploadMock).toHaveBeenCalledTimes(2)
    expect(rowStates(wrapper).map(([name]) => name)).toEqual(['annual-report.pdf', 'deck.pptx'])
  })
})
