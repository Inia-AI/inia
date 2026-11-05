import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import RunFailureNotice from './RunFailureNotice.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

const mountNotice = (props: Record<string, unknown>) =>
  mount(RunFailureNotice, {
    props,
    global: { plugins: [i18n], stubs: { IniaIcon: { template: '<i />' } } },
  })

describe('RunFailureNotice', () => {
  it('states why a refused run failed, in the words the platform wrote', () => {
    const notice = mountNotice({
      failureKind: 'RejectedAtIntake',
      failureDetail: 'Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.',
    })

    expect(notice.find('.run-failure-label').text()).toBe('The report could not use your documents')
    expect(notice.find('.run-failure-body').text()).toBe('Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.')
    expect(notice.find('.run-failure-detail').exists()).toBe(false)
  })

  it('explains an outage in plain words and folds the technical detail away for support', () => {
    const notice = mountNotice({
      failureKind: 'ScreeningUnavailable',
      failureDetail: "The screening judge failed while judging 'statement.pdf'",
    })

    expect(notice.find('.run-failure-label').text()).toBe('Your documents could not be checked')
    expect(notice.find('.run-failure-body').text()).toBe('The report did not start, because our document check is not working right now. This is a problem on our side, not with your documents. Try again in a few minutes.')
    const technicalDetails = notice.find('details.run-failure-detail')
    expect(technicalDetails.attributes('open')).toBeUndefined()
    expect(technicalDetails.find('summary').text()).toBe('Technical details')
    expect(technicalDetails.find('p').text()).toBe("The screening judge failed while judging 'statement.pdf'")
  })

  it('sends a refused reader back to change the documents', async () => {
    const notice = mountNotice({ failureKind: 'RejectedAtIntake', failureDetail: 'Missing document: Custody statement.', canAct: true })

    const action = notice.find('.run-failure-action')
    expect(action.text()).toBe('Change the documents')
    await action.trigger('click')

    expect(notice.emitted('changeDocuments')).toHaveLength(1)
    expect(notice.emitted('runAgain')).toBeUndefined()
  })

  it('offers another run when the document check was down', async () => {
    const notice = mountNotice({ failureKind: 'ScreeningUnavailable', failureDetail: 'judge failed', canAct: true })

    const action = notice.find('.run-failure-action')
    expect(action.text()).toBe('Run again')
    await action.trigger('click')

    expect(notice.emitted('runAgain')).toHaveLength(1)
  })

  it('offers no action when the report cannot be started again from here', () => {
    expect(mountNotice({ failureKind: 'RejectedAtIntake', failureDetail: 'Missing document', canAct: false }).find('.run-failure-action').exists()).toBe(false)
  })

  it('offers no action when only the person who maintains the report type can fix it', () => {
    expect(mountNotice({ failureKind: 'VerificationUnsatisfiable', failureDetail: 'x', canAct: true }).find('.run-failure-action').exists()).toBe(false)
  })

  it('renders nothing for a failure that carries neither a kind nor a detail', () => {
    expect(mountNotice({ failureKind: null, failureDetail: null }).find('.run-failure-notice').exists()).toBe(false)
  })
})
