import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import type {
  WorkflowExecutionDocument,
  WorkflowExecutionVariableValue,
} from '@/api/workflowReports/schema'
import { WorkflowVariableTypes } from '@/enums'
import WorkflowCredits from './WorkflowCredits.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key}:${JSON.stringify(params)}` : key,
  }),
}))

// reka-ui's AccordionContent unmounts when collapsed under jsdom, so stub the
// primitives with passthrough slots so tests can read the content directly.
const passthroughStub = (tag = 'div') =>
  defineComponent({
    setup(_, { slots, attrs }) {
      return () => h(tag, { ...attrs }, slots.default?.())
    },
  })

const accordionStubs = {
  Accordion: passthroughStub('div'),
  AccordionItem: passthroughStub('div'),
  AccordionTrigger: passthroughStub('button'),
  AccordionContent: passthroughStub('div'),
}

const baseExecution = {
  counterpartyName: 'Acme Corp',
  analystName: 'jdoe',
  workflowState: 'Draft',
  startedAt: '2026-05-27T10:00:00Z',
  completedAt: '2026-05-27T10:00:47Z',
  duration: '00:00:47.1000000',
}

const users = {
  value: [{ username: 'jdoe', fullName: 'Jane Doe' }],
}

const inputDoc = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument => ({
  fileId: 'f1',
  name: 'Topics_10_Pages',
  extension: '.pdf',
  documentClass: 'documentClass_2',
  documentClassDisplayName: 'Document Class A',
  sizeBytes: 12345,
  shouldBeDisplayedInOutput: true,
  isOutputDocument: false,
  ...overrides,
})

const outputDoc = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument => ({
  fileId: 'fOut',
  name: 'summary',
  extension: '.docx',
  documentClass: 'output_summary',
  documentClassDisplayName: 'Summary',
  sizeBytes: 4096,
  shouldBeDisplayedInOutput: true,
  isOutputDocument: true,
  ...overrides,
})

const variable = (overrides: Partial<WorkflowExecutionVariableValue> = {}): WorkflowExecutionVariableValue => ({
  replacementTag: 'reviewPeriod',
  displayName: 'Review Period',
  description: null,
  type: WorkflowVariableTypes.String,
  displayValue: 'Q2 2026',
  isConfidential: false,
  isHidden: false,
  isRequired: false,
  ...overrides,
})

const factory = (
  executionData: Record<string, unknown>,
  stubs: Record<string, unknown> = accordionStubs,
) =>
  mount(WorkflowCredits, {
    props: {
      users,
      executionData,
    },
    global: {
      stubs,
    },
  })

const realAccordionFactory = (executionData: Record<string, unknown>) =>
  factory(executionData, {})

describe('WorkflowCredits credits rows', () => {
  it('renders counterparty / analyst / duration rows', () => {
    const wrapper = factory({ ...baseExecution })

    expect(wrapper.text()).toContain('Acme Corp')
    expect(wrapper.text()).toContain('Jane Doe')
    expect(wrapper.text()).toContain('47s')
  })
})

describe('WorkflowCredits documents', () => {
  it('renders input documents section with class display names, not keys', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
    })

    expect(wrapper.text()).toContain('page.workflowDetails.documents.inputLabel')
    expect(wrapper.text()).toContain('Document Class A')
    expect(wrapper.text()).not.toContain('documentClass_2')
    expect(wrapper.text()).toContain('Topics_10_Pages.pdf')
  })

  it('renders output documents section separately when output documents exist', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc(), outputDoc()],
    })

    expect(wrapper.text()).toContain('page.workflowDetails.documents.inputLabel')
    expect(wrapper.text()).toContain('page.workflowDetails.documents.outputLabel')
    expect(wrapper.text()).toContain('Summary')
    expect(wrapper.text()).toContain('summary.docx')
  })

  it('falls back to documentClass key when displayName is missing', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        inputDoc({ documentClassDisplayName: undefined }),
      ],
    })

    expect(wrapper.text()).toContain('documentClass_2')
  })

  it('hides input section when there are no input documents', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [outputDoc()],
    })

    expect(wrapper.text()).not.toContain('page.workflowDetails.documents.inputLabel')
    expect(wrapper.text()).toContain('page.workflowDetails.documents.outputLabel')
  })

  it('hides output section when there are no output documents', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
    })

    expect(wrapper.text()).toContain('page.workflowDetails.documents.inputLabel')
    expect(wrapper.text()).not.toContain('page.workflowDetails.documents.outputLabel')
  })

  it('hides nothing when both sections are empty (no documents render)', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [],
    })

    expect(wrapper.text()).not.toContain('page.workflowDetails.documents.inputLabel')
    expect(wrapper.text()).not.toContain('page.workflowDetails.documents.outputLabel')
  })

  it('lists an input hidden from the report, marked as hidden', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        inputDoc({ shouldBeDisplayedInOutput: false, name: 'hidden_doc' }),
      ],
    })

    expect(wrapper.text()).toContain('hidden_doc')
    expect(wrapper.text()).toContain('page.workflowDetails.documents.hiddenFromOutput')
  })

  it('counts a hidden input in the input section count', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        inputDoc({ fileId: 'f1', name: 'shown_doc' }),
        inputDoc({ fileId: 'f2', name: 'hidden_doc', shouldBeDisplayedInOutput: false }),
      ],
    })

    expect(wrapper.find('.meta-section-count').text()).toBe('2')
  })

  it('still drops an output document marked shouldBeDisplayedInOutput=false', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        outputDoc({ shouldBeDisplayedInOutput: false, name: 'hidden_generated' }),
      ],
    })

    expect(wrapper.text()).not.toContain('hidden_generated')
  })

  it('lists a hidden input that carries no document class', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        inputDoc({
          name: 'unclassified_upload',
          shouldBeDisplayedInOutput: false,
          documentClass: '',
          documentClassDisplayName: '',
        }),
      ],
    })

    expect(wrapper.text()).toContain('unclassified_upload')
    expect(wrapper.text()).toContain('page.workflowDetails.documents.hiddenFromOutput')
  })

  it('does not double the extension when the stored name already carries it', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [
        inputDoc({ name: 'CIS_Reg_Fmk_8_25v2.pdf', extension: '.pdf' }),
      ],
    })

    expect(wrapper.text()).toContain('CIS_Reg_Fmk_8_25v2.pdf')
    expect(wrapper.text()).not.toContain('CIS_Reg_Fmk_8_25v2.pdf.pdf')
  })

  it('still appends the extension when the stored name lacks it', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc({ name: 'Topics_10_Pages', extension: '.pdf' })],
    })

    expect(wrapper.text()).toContain('Topics_10_Pages.pdf')
  })

  it('emits preview when a PDF row is clicked', async () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
    })

    await wrapper.get('[data-testid="document-row-f1"]').trigger('click')

    expect(wrapper.emitted('preview')).toBeTruthy()
    expect(wrapper.emitted('download')).toBeFalsy()
  })

  it('emits download when a non-PDF row is clicked', async () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [outputDoc()],
    })

    await wrapper.get('[data-testid="document-row-fOut"]').trigger('click')

    expect(wrapper.emitted('download')).toBeTruthy()
    expect(wrapper.emitted('preview')).toBeFalsy()
  })

  it('emits download when the download icon is clicked on a PDF row (does not also preview)', async () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
    })

    await wrapper.get('[data-testid="document-download-f1"]').trigger('click')

    expect(wrapper.emitted('download')).toBeTruthy()
    expect(wrapper.emitted('preview')).toBeFalsy()
  })
})

describe('WorkflowCredits documents not used', () => {
  const droppedDoc = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument =>
    inputDoc({
      fileId: 'fDropped',
      name: 'holiday-photo',
      extension: '.png',
      documentClass: '',
      documentClassDisplayName: undefined,
      excludedAt: '2026-09-22T10:00:00Z',
      exclusionReason: 'NotExpectedDocument',
      ...overrides,
    })

  it('lists every document the run did not use, apart from the source documents, each with its reason', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
      excludedDocuments: [droppedDoc()],
    })

    expect(wrapper.find('[data-testid="documents-trigger-excluded"]').text()).toContain('page.workflowDetails.documents.excludedLabel')
    const row = wrapper.find('[data-testid="document-row-fDropped"]')
    expect(row.text()).toContain('holiday-photo.png')
    expect(row.text()).toContain('page.workflowDetails.documents.exclusionReasons.other')
    expect(wrapper.find('[data-testid="document-row-f1"]').text()).toContain('Document Class A')
  })

  it('opens the list by default, so a dropped file is never missed', () => {
    const wrapper = realAccordionFactory({
      ...baseExecution,
      excludedDocuments: [droppedDoc()],
    })

    expect(wrapper.find('[data-testid="documents-trigger-excluded"] button').attributes('aria-expanded')).toBe('true')
    expect(wrapper.text()).toContain('holiday-photo.png')
  })

  it('lets the reader open a dropped file, because it arrived', async () => {
    const wrapper = factory({
      ...baseExecution,
      excludedDocuments: [droppedDoc({ extension: '.pdf' })],
    })

    await wrapper.find('[data-testid="document-row-fDropped"]').trigger('click')

    expect(wrapper.emitted('preview')).toHaveLength(1)
  })

  it('shows no list when the run used everything it was sent', () => {
    const wrapper = factory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
      excludedDocuments: [],
    })

    expect(wrapper.find('[data-testid="documents-trigger-excluded"]').exists()).toBe(false)
  })
})

describe('WorkflowCredits variables section', () => {
  it('does not render the variables section when variableValues is empty', () => {
    const wrapper = factory({ ...baseExecution, variableValues: [] })
    expect(wrapper.find('[data-testid="variables-section"]').exists()).toBe(false)
  })

  it('does not render the variables section when all entries are hidden', () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [variable({ isHidden: true })],
    })
    expect(wrapper.find('[data-testid="variables-section"]').exists()).toBe(false)
  })

  it('renders the variables section with the visible count in the trigger', () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [
        variable({ replacementTag: 'a', displayName: 'A' }),
        variable({ replacementTag: 'b', displayName: 'B' }),
        variable({ replacementTag: 'c', displayName: 'C', isHidden: true }),
      ],
    })
    expect(wrapper.find('[data-testid="variables-section"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('page.workflowDetails.variables.label')
    expect(wrapper.text()).toContain('2')
  })

  it('renders variable rows with displayName and displayValue when expanded', async () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [variable({ displayName: 'Review Period', displayValue: 'Q2 2026' })],
    })

    await wrapper.get('[data-testid="variables-trigger"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Review Period')
    expect(wrapper.text()).toContain('Q2 2026')
  })

  it('renders the placeholder key when displayValue is null', async () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [variable({ displayValue: null })],
    })

    await wrapper.get('[data-testid="variables-trigger"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('page.workflowDetails.variables.noValuePlaceholder')
  })

  it('passes confidential mask through as-is from the server', async () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [
        variable({
          displayName: 'API Key',
          displayValue: '••••',
          isConfidential: true,
        }),
      ],
    })

    await wrapper.get('[data-testid="variables-trigger"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('••••')
  })

  it('excludes hidden variables from the rendered list', async () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [
        variable({ displayName: 'Visible One', displayValue: 'yes' }),
        variable({ displayName: 'Hidden One', displayValue: 'no', isHidden: true }),
      ],
    })

    await wrapper.get('[data-testid="variables-trigger"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Visible One')
    expect(wrapper.text()).not.toContain('Hidden One')
  })

  it('renders an info icon when description is provided', async () => {
    const wrapper = factory({
      ...baseExecution,
      variableValues: [
        variable({ displayName: 'With Desc', description: 'Has description' }),
        variable({ replacementTag: 'b', displayName: 'No Desc' }),
      ],
    })

    await wrapper.get('[data-testid="variables-trigger"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="variable-info-reviewPeriod"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="variable-info-b"]').exists()).toBe(false)
  })
})

describe('WorkflowCredits created row', () => {
  it('renders the creation time between the analyst and duration rows', () => {
    const wrapper = factory({ ...baseExecution, createdAt: '2026-05-27T12:00:00Z' })

    const labels = wrapper.findAll('.meta-label').map(node => node.text())
    const createdIndex = labels.indexOf('page.workflowDetails.credit.createdLabel')
    const analystIndex = labels.indexOf('page.workflowDetails.credit.creditAnalystLabel')
    const durationIndex = labels.indexOf('page.workflowDetails.credit.durationLabel')

    expect(createdIndex).toBeGreaterThan(analystIndex)
    expect(createdIndex).toBeLessThan(durationIndex)
  })

  it('formats the creation time as dd/MM/yyyy HH:mm', () => {
    const wrapper = factory({ ...baseExecution, createdAt: '2026-05-27T12:00:00Z' })

    expect(wrapper.text()).toMatch(/27\/05\/2026 \d{2}:\d{2}/)
  })

  it('renders the empty placeholder when the report has no creation time', () => {
    const wrapper = factory({ ...baseExecution, createdAt: undefined })

    const createdRow = wrapper
      .findAll('.meta-row')
      .find(row => row.text().includes('page.workflowDetails.credit.createdLabel'))

    expect(createdRow?.find('.meta-value').text()).toBe('-')
  })
})

describe('WorkflowCredits document section disclosure', () => {
  const bothSections = () =>
    realAccordionFactory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc(), outputDoc()],
    })

  const openState = (wrapper: ReturnType<typeof realAccordionFactory>, section: string) =>
    wrapper.find(`[data-testid="documents-trigger-${section}"] button`).attributes('aria-expanded')

  it('starts source documents collapsed and generated documents expanded, with both counts readable', () => {
    const wrapper = bothSections()

    expect(openState(wrapper, 'source')).toBe('false')
    expect(openState(wrapper, 'generated')).toBe('true')
    expect(wrapper.find('[data-testid="documents-trigger-source"]').text()).toContain('1')
    expect(wrapper.find('[data-testid="documents-trigger-generated"]').text()).toContain('1')
  })

  it('leaves generated documents open when source documents are opened', async () => {
    const wrapper = bothSections()

    await wrapper.find('[data-testid="documents-trigger-source"] button').trigger('click')
    await flushPromises()

    expect(openState(wrapper, 'source')).toBe('true')
    expect(openState(wrapper, 'generated')).toBe('true')
  })

  it('collapses source documents even when the report has no generated documents', () => {
    const wrapper = realAccordionFactory({
      ...baseExecution,
      workflowExecutionDocuments: [inputDoc()],
    })

    expect(openState(wrapper, 'source')).toBe('false')
  })
})
