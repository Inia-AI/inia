import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import { describeRunFailure } from './runFailure'

const { t } = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false }).global

describe('describeRunFailure', () => {
  it('says in plain words that the documents could not be used, and shows the reason the platform wrote', () => {
    expect(describeRunFailure(t, 'RejectedAtIntake', 'Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.')).toEqual({
      label: 'The report could not use your documents',
      body: 'Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.',
      detail: null,
      action: 'changeDocuments',
    })
  })

  it('tells an outage apart in plain words and keeps the technical detail secondary', () => {
    expect(describeRunFailure(t, 'ScreeningUnavailable', "The screening judge failed while judging 'statement.pdf': Model ID is not supported")).toEqual({
      label: 'Your documents could not be checked',
      body: 'The report did not start, because our document check is not working right now. This is a problem on our side, not with your documents. Try again in a few minutes.',
      detail: "The screening judge failed while judging 'statement.pdf': Model ID is not supported",
      action: 'runAgain',
    })
  })

  it('gives every failure kind the platform writes a label of its own', () => {
    const labels = ['CriticalBlockFailure', 'ResultsLost', 'LifetimeExceeded', 'HostLost', 'VerificationGroupShape', 'VerificationUnsatisfiable', 'RejectedAtIntake', 'ScreeningUnavailable']
      .map(kind => describeRunFailure(t, kind, 'detail')?.label)

    expect(new Set(labels).size).toBe(labels.length)
    expect(labels).not.toContain('The report did not finish')
  })

  it('words a failure it cannot classify in plain advice, and folds the raw text away as the detail', () => {
    const plainAdvice = {
      label: 'The report did not finish',
      body: 'Something went wrong while the report ran. Run it again. If it fails again, contact support.',
      detail: 'NullReferenceException: Object reference not set to an instance of an object.',
      action: 'runAgain',
    }

    expect(describeRunFailure(t, 'SomethingNew', plainAdvice.detail)).toEqual(plainAdvice)
    expect(describeRunFailure(t, null, plainAdvice.detail)).toEqual(plainAdvice)
  })

  it('offers another run for every kind a second run can fix, and nothing where only the report type can change', () => {
    const actionOf = (kind: string) => describeRunFailure(t, kind, 'detail')?.action

    expect(['CriticalBlockFailure', 'ResultsLost', 'LifetimeExceeded', 'HostLost', 'ScreeningUnavailable'].map(actionOf))
      .toEqual(['runAgain', 'runAgain', 'runAgain', 'runAgain', 'runAgain'])
    expect(actionOf('VerificationGroupShape')).toBeNull()
    expect(actionOf('VerificationUnsatisfiable')).toBeNull()
  })

  it('has nothing to say about a failure that carries neither a kind nor a detail', () => {
    expect(describeRunFailure(t, null, null)).toBeNull()
    expect(describeRunFailure(t, undefined, '   ')).toBeNull()
  })
})
