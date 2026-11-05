import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { TokenUsage } from '@/api/tokenUsage/schema'

const USAGE: TokenUsage = {
  id: 'u1',
  organizationId: 'IniaAI',
  userId: 'user-1',
  userDisplayName: 'Analyst One',
  workflowTypeId: null,
  workflowTypeName: 'Credit Memo',
  workflowExecutionId: null,
  blockId: null,
  documentId: null,
  usageType: 'LlmBlockExecution',
  executionContext: '',
  blockType: null,
  provider: 'aws-bedrock',
  modelId: 'claude-sonnet',
  components: [{ componentType: 'Input', units: 1200, cost: 0.0036 }],
  totalCost: 0.0036,
  createdAt: '2026-09-28T14:16:00Z',
  description: null,
} as TokenUsage

const PERIOD = {
  period: 'thisMonth',
  periodStart: '2026-09-01T00:00:00Z',
  periodEnd: '2026-09-30T23:59:59Z',
  totalInputTokens: 1200,
  totalOutputTokens: 0,
  totalTokens: 1200,
  totalCacheCreationTokens: 0,
  totalCacheReadTokens: 0,
  totalCost: 0.0036,
  operationCount: 1,
}

vi.mock('@/api', () => ({
  default: {
    TokenUsage: {
      service: {
        getOrganizationDashboard: vi.fn(async () => null),
        getMyUsageSummary: vi.fn(async () => ({
          userId: 'user-1',
          userDisplayName: 'Analyst One',
          today: PERIOD,
          thisWeek: PERIOD,
          thisMonth: PERIOD,
          allTime: PERIOD,
          byUsageType: [],
        })),
        listTokenUsage: vi.fn(async () => ({ value: [USAGE], count: 1 })),
      },
    },
  },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

import TokenUsageAnalytics from './TokenUsageAnalytics.vue'

describe('TokenUsageAnalytics details', () => {
  it('expands a usage row from a native button that is named for the row and says whether it is open', async () => {
    const wrapper = mount(TokenUsageAnalytics, {
      global: { stubs: { TrendLineChart: true } },
      attachTo: document.body,
    })
    await flushPromises()
    await wrapper.findAll('[role="tab"]').find(tab => tab.text() === 'Details')!.trigger('mousedown', { button: 0 })
    await flushPromises()

    const expand = wrapper.get('.details-row .col-expand button')
    expect(expand.attributes('type')).toBe('button')
    expect(expand.attributes('aria-expanded')).toBe('false')
    expect(expand.attributes('aria-label')).toMatch(/^page\.tokenUsage\.details\.breakdownFor:/)
    expect(wrapper.find('.details-expanded').exists()).toBe(false)

    await expand.trigger('click')

    expect(expand.attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('.details-expanded').text()).toContain('Analyst One')
    wrapper.unmount()
  })
})
