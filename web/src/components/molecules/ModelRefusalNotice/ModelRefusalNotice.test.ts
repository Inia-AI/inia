import { describe, expect, it } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import ModelRefusalNotice from './ModelRefusalNotice.vue'
import type { WorkflowExecutionLogSummary } from '@/api/workflowExecutions/schema'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

const mountNotice = (logs: WorkflowExecutionLogSummary[]) =>
  mount(ModelRefusalNotice, {
    props: { executionId: 'exec-1', logs },
    global: { plugins: [i18n], stubs: { IniaIcon: { template: '<i />' }, RouterLink: RouterLinkStub } },
  })

const log = (patch: Partial<WorkflowExecutionLogSummary>): WorkflowExecutionLogSummary => ({
  id: patch.blockName ?? 'log',
  blockName: 'Block',
  status: 'Completed',
  ...patch,
})

describe('ModelRefusalNotice', () => {
  it('names each refused step and why, and says its output is missing', () => {
    const notice = mountNotice([
      log({ blockName: 'Extract_Cash', sectionName: 'Cash', status: 'Failed', refusalCategory: 'reasoning_extraction' }),
      log({ blockName: 'ID.jpg, page 1', sectionName: 'Document intake', status: 'Failed', refusalCategory: 'written' }),
      log({ blockName: 'PL_Revenue', status: 'Completed' }),
      log({ blockName: 'Extract_Equity', status: 'Failed', error: 'Error response from LLM' }),
    ])

    expect(notice.find('.model-refusal-label').text()).toBe('The model refused 2 steps of this run')
    const items = notice.findAll('.model-refusal-list li').map(item => item.text().replace(/\s+/g, ' '))
    expect(items).toEqual([
      'Extract_Cash in Cash (refusal category: reasoning_extraction)',
      'ID.jpg, page 1 in Document intake (it wrote a refusal in place of an answer)',
    ])
    expect(notice.findComponent(RouterLinkStub).props('to')).toEqual({ name: 'ExecutionLogs', params: { id: 'exec-1' } })
  })

  it('lists a block refused in several loop iterations once, with how many times', () => {
    const notice = mountNotice([
      log({ id: 'a', blockName: 'Extract_Fund', sectionName: 'Funds', status: 'Failed', refusalCategory: 'cyber' }),
      log({ id: 'b', blockName: 'Extract_Fund', sectionName: 'Funds', status: 'Failed', refusalCategory: 'cyber' }),
    ])

    expect(notice.find('.model-refusal-label').text()).toBe('The model refused 2 steps of this run')
    const items = notice.findAll('.model-refusal-list li').map(item => item.text().replace(/\s+/g, ' '))
    expect(items).toEqual(['Extract_Fund in Funds (refusal category: cyber) (2 times)'])
  })

  it('uses the singular for one refused step', () => {
    const notice = mountNotice([log({ blockName: 'Extract_Cash', status: 'Failed', refusalCategory: 'cyber' })])

    expect(notice.find('.model-refusal-label').text()).toBe('The model refused one step of this run')
  })

  it('shows nothing when the model refused nothing', () => {
    const notice = mountNotice([log({ status: 'Failed', error: 'Timeout' }), log({ status: 'Completed' })])

    expect(notice.find('.model-refusal-notice').exists()).toBe(false)
  })
})
