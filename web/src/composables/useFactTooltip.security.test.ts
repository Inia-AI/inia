import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'

import { useFactTooltip } from './useFactTooltip'
import type { Citation } from '@/api/workflows/schema'
import { escapeHtml } from '@/lib/escapeHtml'
import { markupProblems } from '@/test/helpers/inertMarkup'

// The fact popup builds its own markup from a report's citations, which model output over a
// customer's document fills. Every value must reach the page as text: no element that runs,
// no handler attribute, no script address.

const MARKUP = '<img src=x onerror=1>'
const BREAKOUT = '" onmouseover="1'
const SCRIPT_ADDRESS = 'javascript:alert(1)'
const PLAIN = 'annual-report.pdf'
let wrapper: VueWrapper | null = null

function openPopup(html: string, citations: Citation[] = []): HTMLElement {
  const Paragraph = defineComponent({
    setup() {
      const scope = ref<HTMLElement | null>(null)
      useFactTooltip(scope)
      return () => h('div', { ref: scope, 'data-citations': JSON.stringify(citations), innerHTML: html })
    },
  })
  wrapper = mount(Paragraph, { attachTo: document.body })
  wrapper.element.querySelector('fact')?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  return document.getElementById('fact-tooltip-container') as HTMLElement
}

const problemsOf = (popup: HTMLElement): string[] => markupProblems([...popup.querySelectorAll('*')], ['img'])

const legacySource = (type: string, label: string, citedText: string) =>
  `<fact_source type="${type}" label="${escapeHtml(label)}"><cited-text>${escapeHtml(citedText)}</cited-text></fact_source>`

afterEach(() => {
  document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
  wrapper?.unmount()
  wrapper = null
})

describe('useFactTooltip security', () => {
  it.each([
    ['a document label holding markup', legacySource('document', MARKUP, MARKUP)],
    ['a document label that closes its attribute', legacySource('document', BREAKOUT, BREAKOUT)],
    ['a block label holding markup', legacySource('block', MARKUP, 'excerpt')],
    ['a web source with a script address', legacySource('web', SCRIPT_ADDRESS, 'excerpt')],
  ])('shows %s of a legacy fact as text', (_, source) => {
    const popup = openPopup(`Net assets <fact>1,234,567${source}${legacySource('document', PLAIN, 'excerpt')}</fact>.`)

    expect(problemsOf(popup)).toEqual([])
    expect(popup.textContent).toContain(PLAIN)
  })

  it('shows the values of a structured citation as text', () => {
    const citations: Citation[] = [
      {
        id: 'c0',
        sources: [
          { type: 'Document', pageId: `p1${BREAKOUT}`, pageNumber: 1, documentName: MARKUP, citedText: MARKUP },
          { type: 'Url', url: SCRIPT_ADDRESS, citedText: BREAKOUT },
          { type: 'Document', pageId: 'p2', pageNumber: 2, documentName: PLAIN, citedText: 'excerpt' },
        ],
      },
    ]

    const popup = openPopup('Net assets <fact id="c0">1,234,567</fact>.', citations)

    expect(problemsOf(popup)).toEqual([])
    expect(popup.textContent).toContain(PLAIN)
  })
})
