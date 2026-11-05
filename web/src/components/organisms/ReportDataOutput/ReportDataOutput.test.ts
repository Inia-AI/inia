import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import ReportDataOutput from './ReportDataOutput.vue'
import type { ReportDataOutput as DataOutput } from '@/api/workflowReports/schema'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

const output: DataOutput = {
  name: 'facilities',
  data: {
    document_assessment: [{ file: 'statement.pdf', readable: true }],
    account_movement: { a1: { total_credits: 318204.4, cheques_returned_count: 7 } },
  },
  citations: [
    {
      id: 'c0',
      path: '/account_movement/a1/total_credits',
      value: '318204.4',
      sources: [{ type: 'Document', citedText: 'total credits 318,204.40', pageId: 'page-2', pageNumber: 2, documentName: 'Bank statement.pdf' }],
    },
  ],
  citedSourcesByBlock: [],
}

let wrapper: VueWrapper | null = null

const mountOutput = () => {
  wrapper = mount(ReportDataOutput, {
    props: { output },
    attachTo: document.body,
    global: { plugins: [i18n] },
  })
  return wrapper
}

afterEach(() => {
  document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  wrapper?.unmount()
  wrapper = null
  vi.unstubAllGlobals()
})

describe('ReportDataOutput', () => {
  it('shows the nested JSON itself, indented', () => {
    const json = mountOutput().get('.data-output__json')

    expect(json.text()).toBe(JSON.stringify(output.data, null, 2))
  })

  it('opens the sources of a cited figure, as a report figure does', async () => {
    const fact = mountOutput().get('.data-output__json fact')
    await flushPromises()

    expect(fact.text()).toBe('318204.4')
    fact.element.dispatchEvent(new MouseEvent('click', { bubbles: true }))

    expect(document.getElementById('fact-tooltip-container')?.textContent).toContain('Bank statement.pdf')
  })

  it('copies the JSON a reading system receives', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })

    await mountOutput().findAll('button').find(button => button.text() === 'Copy JSON')!.trigger('click')

    expect(writeText).toHaveBeenCalledWith(JSON.stringify(output.data, null, 2))
  })
})
