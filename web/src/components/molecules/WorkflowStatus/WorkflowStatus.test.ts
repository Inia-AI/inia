import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import { WorkflowState } from '@/enums'
import WorkflowStatus from './WorkflowStatus.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

const accessibleName = (props: Record<string, unknown>) =>
  mount(WorkflowStatus, {
    props,
    global: {
      plugins: [i18n],
      stubs: { IniaIcon: { template: '<i />' }, IniaLoader: { template: '<i />' } },
    },
  }).find('[role="img"]').attributes('aria-label')

describe('WorkflowStatus', () => {
  it('names a refused run and its reason, so it reads apart from an outage', () => {
    const refused = accessibleName({
      status: WorkflowState.Failed,
      failureKind: 'RejectedAtIntake',
      failureDetail: 'Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.',
    })
    const outage = accessibleName({
      status: WorkflowState.Failed,
      failureKind: 'ScreeningUnavailable',
      failureDetail: "The screening judge failed while judging 'statement.pdf'",
    })

    expect(refused).toBe('Failed: The report could not use your documents. Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.')
    expect(outage).toBe('Failed: Your documents could not be checked. The report did not start, because our document check is not working right now. This is a problem on our side, not with your documents. Try again in a few minutes.')
  })

  it('keeps the generic description for a failure the platform could not classify', () => {
    expect(accessibleName({ status: WorkflowState.Failed })).toBe(
      'Failed: The run errored out — open the report for details.',
    )
  })

  it('ignores failure fields on a run that did not fail', () => {
    expect(accessibleName({ status: WorkflowState.Draft, failureKind: 'RejectedAtIntake', failureDetail: 'stale' })).toBe(
      'Ready to review: Output is ready for your review.',
    )
  })
})
