/**
 * The edit dialog marks a document static for a workflow type and a document class, so every run
 * of that type in the organization reads it, or clears the mark.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { dialogStubs } from '@/test/helpers/rekaStubs'
import type { DocumentSummary } from '@/api/documents/schema'

const { updateMock, toastMock } = vi.hoisted(() => ({ updateMock: vi.fn(), toastMock: vi.fn() }))

vi.mock('@/api', async () => {
  const { ref: vueRef, computed, unref } = await vi.importActual<typeof import('vue')>('vue')
  const summaries: Record<string, unknown> = {
    AssetManagerCreditAnalysis: {
      documentClasses: [
        { key: 'Regulatory_Fmk_Updated', displayName: 'Regulatory framework', isOutputDocument: false },
        { key: 'Annual_Report', displayName: 'Annual report', isOutputDocument: false },
        { key: 'Credit_Memo', displayName: 'Credit memo', isOutputDocument: true },
      ],
    },
    CorporateCredit: { documentClasses: [{ key: 'Financials', displayName: 'Financials', isOutputDocument: false }] },
    OutputOnly: { documentClasses: [{ key: 'Memo', displayName: 'Memo', isOutputDocument: true }] },
  }
  return {
    default: {
      Workflows: {
        useGetWorkflowTypeOptions: () => ({
          data: vueRef([
            { workflowTypeId: 'AssetManagerCreditAnalysis', workflowTypeName: 'Asset Manager Credit Analysis' },
            { workflowTypeId: 'CorporateCredit', workflowTypeName: 'Corporate Credit' },
            { workflowTypeId: 'OutputOnly', workflowTypeName: 'Output Only' },
          ]),
        }),
        useGetWorkflowSummaryByType: (id: unknown) => ({
          data: computed(() => summaries[unref(id) as string] ?? null),
        }),
      },
      Documents: {
        useUpdateDocument: () => ({ mutateAsync: updateMock, isPending: vueRef(false) }),
      },
    },
  }
})

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: toastMock }) }))

import EditDocumentDialog from './EditDocumentDialog.vue'

const SelectStub = defineComponent({
  name: 'IniaSelect',
  props: { modelValue: String, options: Array },
  emits: ['update:modelValue'],
  setup(props, { emit, attrs }) {
    return () =>
      h(
        'select',
        {
          'data-testid': attrs['data-testid'],
          value: props.modelValue,
          onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLSelectElement).value),
        },
        (props.options as { label: string; value: string }[]).map(option =>
          h('option', { value: option.value }, option.label),
        ),
      )
  },
})

const SwitchStub = defineComponent({
  name: 'SwitchStub',
  props: { modelValue: Boolean },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        type: 'checkbox',
        'data-testid': 'static-switch',
        checked: props.modelValue,
        onChange: (event: Event) => emit('update:modelValue', (event.target as HTMLInputElement).checked),
      })
  },
})

const document = (overrides: Partial<DocumentSummary> = {}): DocumentSummary => ({
  id: 'doc-1',
  name: 'framework.pdf',
  mediaType: 'ApplicationPdf',
  sizeBytes: 1024,
  extension: '.pdf',
  userId: null,
  documentCategoryId: null,
  workflowTypeId: null,
  class: null,
  processingState: 'Processed',
  processingError: null,
  createdAt: '2026-10-06T10:00:00Z',
  updatedAt: null,
  ...overrides,
})

const mountDialog = async (doc: DocumentSummary) => {
  const wrapper = mount(EditDocumentDialog, {
    props: { open: true, document: doc },
    global: { stubs: { ...dialogStubs, IniaSelect: SelectStub, Switch: SwitchStub } },
  })
  await flushPromises()
  return wrapper
}

describe('EditDocumentDialog', () => {
  beforeEach(() => {
    updateMock.mockReset().mockResolvedValue({})
    toastMock.mockReset()
  })

  it('shows a static document with its workflow type and class', async () => {
    const wrapper = await mountDialog(document({ workflowTypeId: 'AssetManagerCreditAnalysis', class: 'Regulatory_Fmk_Updated' }))

    expect((wrapper.find('[data-testid="static-switch"]').element as HTMLInputElement).checked).toBe(true)
    expect((wrapper.find('[data-testid="workflow-type"]').element as HTMLSelectElement).value).toBe('AssetManagerCreditAnalysis')
    expect((wrapper.find('[data-testid="document-class"]').element as HTMLSelectElement).value).toBe('Regulatory_Fmk_Updated')
  })

  it('forgets the class when another workflow type is chosen, even one with no input classes', async () => {
    const wrapper = await mountDialog(document({ workflowTypeId: 'AssetManagerCreditAnalysis', class: 'Regulatory_Fmk_Updated' }))

    await wrapper.find('[data-testid="workflow-type"]').setValue('OutputOnly')
    await flushPromises()

    expect((wrapper.find('[data-testid="document-class"]').element as HTMLSelectElement).value).toBe('')
    expect(wrapper.find('[data-testid="save-document"]').attributes('disabled')).toBeDefined()
  })

  it('offers only the input classes of the chosen workflow type', async () => {
    const wrapper = await mountDialog(document())
    await wrapper.find('[data-testid="static-switch"]').setValue(true)
    await wrapper.find('[data-testid="workflow-type"]').setValue('AssetManagerCreditAnalysis')
    await flushPromises()

    const classes = wrapper.findAll('[data-testid="document-class"] option').map(option => option.attributes('value'))
    expect(classes).toEqual(['Annual_Report', 'Regulatory_Fmk_Updated'])
  })

  it('marks a document static for a workflow type and class', async () => {
    const wrapper = await mountDialog(document())
    expect(wrapper.find('[data-testid="save-document"]').attributes('disabled')).toBeUndefined()

    await wrapper.find('[data-testid="static-switch"]').setValue(true)
    expect(wrapper.find('[data-testid="save-document"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[data-testid="workflow-type"]').setValue('AssetManagerCreditAnalysis')
    await flushPromises()
    await wrapper.find('[data-testid="document-class"]').setValue('Regulatory_Fmk_Updated')
    await wrapper.find('[data-testid="save-document"]').trigger('click')
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith({
      id: 'doc-1',
      data: { isStatic: true, workflowTypeId: 'AssetManagerCreditAnalysis', class: 'Regulatory_Fmk_Updated' },
    })
    expect(wrapper.emitted('update:open')).toEqual([[false]])
  })

  it('clears the static mark', async () => {
    const wrapper = await mountDialog(document({ workflowTypeId: 'AssetManagerCreditAnalysis', class: 'Regulatory_Fmk_Updated' }))

    await wrapper.find('[data-testid="static-switch"]').setValue(false)
    await wrapper.find('[data-testid="save-document"]').trigger('click')
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith({ id: 'doc-1', data: { isStatic: false } })
  })

  it('a new workflow type drops a class that type does not read', async () => {
    const wrapper = await mountDialog(document({ workflowTypeId: 'AssetManagerCreditAnalysis', class: 'Regulatory_Fmk_Updated' }))

    await wrapper.find('[data-testid="workflow-type"]').setValue('CorporateCredit')
    await flushPromises()

    expect((wrapper.find('[data-testid="document-class"]').element as HTMLSelectElement).value).toBe('')
    expect(wrapper.find('[data-testid="save-document"]').attributes('disabled')).toBeDefined()
  })
})
