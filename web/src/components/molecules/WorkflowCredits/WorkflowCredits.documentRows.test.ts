import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import type { WorkflowExecutionDocument } from '@/api/workflowReports/schema'
import WorkflowCredits from './WorkflowCredits.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: EN },
  missingWarn: false,
  fallbackWarn: false,
})

const DOCUMENTS = EN.page.workflowDetails.documents

const sourcePdf = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument => ({
  fileId: 'fSource',
  name: 'Annual_Report_2025',
  extension: '.pdf',
  documentClass: 'annual_report',
  documentClassDisplayName: 'Annual Report',
  sizeBytes: 12345,
  shouldBeDisplayedInOutput: true,
  isOutputDocument: false,
  ...overrides,
})

const generatedDocx = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument => ({
  fileId: 'fGenerated',
  name: 'Credit_Memo',
  extension: '.docx',
  documentClass: 'credit_memo',
  documentClassDisplayName: 'Credit Memo',
  sizeBytes: 4096,
  shouldBeDisplayedInOutput: true,
  isOutputDocument: true,
  ...overrides,
})

const droppedPng = (overrides: Partial<WorkflowExecutionDocument> = {}): WorkflowExecutionDocument => ({
  fileId: 'fDropped',
  name: 'holiday-photo',
  extension: '.png',
  documentClass: '',
  sizeBytes: 2048,
  isOutputDocument: false,
  excludedAt: '2026-09-22T10:00:00Z',
  exclusionReason: 'NotExpectedDocument',
  ...overrides,
})

let wrapper: VueWrapper | null = null

const mountCredits = async (executionData: Record<string, unknown>) => {
  wrapper = mount(WorkflowCredits, {
    attachTo: document.body,
    props: {
      users: { value: [] },
      executionData: { counterpartyName: 'Acme Corp', analystName: 'jdoe', ...executionData },
    },
    global: { plugins: [i18n] },
  })
  await flushPromises()
  return wrapper
}

const openSection = async (section: string) => {
  await wrapper!.get(`[data-testid="documents-trigger-${section}"] button`).trigger('click')
  await flushPromises()
}

const textOf = (ids: string | null) =>
  (ids ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .map(id => document.getElementById(id)?.textContent?.trim() ?? `<missing #${id}>`)
    .join(' ')

const accessibleName = (element: Element) => element.getAttribute('aria-label')

const accessibleDescription = (element: Element) => textOf(element.getAttribute('aria-describedby'))

const rowButton = (fileId: string) => document.querySelector<HTMLElement>(`[data-testid="document-row-${fileId}"]`)!
const downloadButton = (fileId: string) =>
  document.querySelector<HTMLElement>(`[data-testid="document-download-${fileId}"]`)!

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

describe('WorkflowCredits document rows for assistive technology', () => {
  it('makes a source PDF row a native button named for its preview by the class and file name it shows', async () => {
    await mountCredits({ workflowExecutionDocuments: [sourcePdf()] })
    await openSection('source')

    const row = rowButton('fSource')
    expect(row.tagName).toBe('BUTTON')
    expect(row.getAttribute('type')).toBe('button')
    expect(accessibleName(row)).toBe('Preview Annual Report Annual_Report_2025.pdf')
    expect(row.hasAttribute('aria-describedby')).toBe(false)
  })

  it('names a generated row that is not a PDF for its download', async () => {
    await mountCredits({ workflowExecutionDocuments: [generatedDocx()] })

    expect(accessibleName(rowButton('fGenerated'))).toBe('Download Credit Memo Credit_Memo.docx')
  })

  it('describes a document the run did not use with the reason it was not used', async () => {
    await mountCredits({ excludedDocuments: [droppedPng()] })

    const row = rowButton('fDropped')
    expect(accessibleName(row)).toBe('Download holiday-photo.png')
    expect(accessibleDescription(row)).toBe(DOCUMENTS.exclusionReasons.NotExpectedDocument)
  })

  it('describes a source document that is hidden from the report as hidden', async () => {
    await mountCredits({ workflowExecutionDocuments: [sourcePdf({ shouldBeDisplayedInOutput: false })] })
    await openSection('source')

    expect(accessibleDescription(rowButton('fSource'))).toBe(DOCUMENTS.hiddenFromOutput)
  })

  it('puts the Download button beside the row button, not inside it, named for its file', async () => {
    await mountCredits({ workflowExecutionDocuments: [sourcePdf()] })
    await openSection('source')

    const row = rowButton('fSource')
    const download = downloadButton('fSource')
    expect(row.contains(download)).toBe(false)
    expect(row.querySelector('button, [role="button"], a, [tabindex]')).toBeNull()
    expect(row.closest('[role="button"]')).toBeNull()
    expect(download.getAttribute('type')).toBe('button')
    expect(download.getAttribute('aria-label')).toBe('Download Annual_Report_2025.pdf')
  })

  it('leaves no document row that is a scripted button', async () => {
    await mountCredits({
      workflowExecutionDocuments: [sourcePdf(), generatedDocx()],
      excludedDocuments: [droppedPng()],
    })
    await openSection('source')

    expect(document.querySelectorAll('li[role="button"], li[tabindex]')).toHaveLength(0)
    expect(document.querySelectorAll('[data-testid^="document-row-"]')).toHaveLength(3)
  })

  it('previews a PDF from its row button and downloads it from its Download button', async () => {
    const credits = await mountCredits({ workflowExecutionDocuments: [sourcePdf()] })
    await openSection('source')

    rowButton('fSource').click()
    downloadButton('fSource').click()
    await flushPromises()

    expect(credits.emitted('preview')).toHaveLength(1)
    expect(credits.emitted('download')).toHaveLength(1)
  })
})
