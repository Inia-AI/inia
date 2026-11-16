import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import type { DocumentClass } from '@/api/workflows/schema'
import { extensionPoints } from '@/modules/extensionPoints'
import { EN } from '@/test/helpers/messages'
import DocumentClassChipLabel from './DocumentClassChipLabel.vue'

const spread: DocumentClass = {
  key: 'CorporateFinancialModel',
  displayName: 'Financial Spreads',
  description: '',
  requirement: 'Required',
  shouldBeSingular: true,
  isOutputDocument: false,
}

const Note = defineComponent({
  props: { documentClass: { type: Object, required: true }, empty: { type: Boolean, required: true } },
  setup: props => () => h('span', { 'data-testid': 'note' }, `${props.documentClass.key} ${props.empty ? 'empty' : 'filled'}`),
})

const mountFor = (documentClass: DocumentClass, empty: boolean) =>
  mount({ components: { DocumentClassChipLabel }, template: '<span><DocumentClassChipLabel :document-class="documentClass" :empty="empty" /></span>', data: () => ({ documentClass, empty }) }, {
    global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN as unknown as Record<string, string> } })] },
  })

afterEach(() => {
  extensionPoints.documentClassNotes.length = 0
})

describe('The label of a document class on the documents step', () => {
  it('names the class and says when it takes one file only', () => {
    expect(mountFor(spread, true).text()).toBe('Financial Spreads· 1 only')
    expect(mountFor({ ...spread, shouldBeSingular: false }, true).text()).toBe('Financial Spreads')
  })

  it('gives each module note the class and whether a document is attached to it', () => {
    extensionPoints.documentClassNotes.push(Note)

    expect(mountFor(spread, true).get('[data-testid="note"]').text()).toBe('CorporateFinancialModel empty')
    expect(mountFor(spread, false).get('[data-testid="note"]').text()).toBe('CorporateFinancialModel filled')
  })
})
