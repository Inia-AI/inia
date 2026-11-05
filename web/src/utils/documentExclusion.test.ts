import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import { describeExclusionReason } from './documentExclusion'

const { t } = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false }).global

describe('describeExclusionReason', () => {
  it('says in plain words why each document was not used', () => {
    expect(describeExclusionReason(t, 'Unreadable')).toBe('Could not be read')
    expect(describeExclusionReason(t, 'NotExpectedDocument')).toBe('Not a document this report uses')
    expect(describeExclusionReason(t, 'NotNeeded')).toBe('Not needed, another document already covers it')
  })

  it('never shows a code it does not know', () => {
    expect(describeExclusionReason(t, 'SomethingNew')).toBe('Not used by this report')
    expect(describeExclusionReason(t, null)).toBe('Not used by this report')
  })
})
