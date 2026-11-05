import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key}:${JSON.stringify(params)}` : key,
  }),
}))

import ReplayProvenanceBadge from './ReplayProvenanceBadge.vue'
import { WorkflowState } from '@/enums'

const stubs = { IniaIcon: { template: '<i />' } }

describe('ReplayProvenanceBadge', () => {
  it('renders nothing for a normal execution', () => {
    const wrapper = mount(ReplayProvenanceBadge, {
      props: { freezeSourceExecutionId: null },
      global: { stubs },
    })
    expect(wrapper.find('.replay-provenance-badge').exists()).toBe(false)
  })

  it('renders the badge with the source and coverage for a frozen execution', () => {
    const wrapper = mount(ReplayProvenanceBadge, {
      props: {
        freezeSourceExecutionId: 'exec-baseline',
        freezeReplayedCount: 2,
        freezeEligibleCount: 3,
      },
      global: { stubs },
    })
    const badge = wrapper.find('.replay-provenance-badge')
    expect(badge.exists()).toBe(true)
    expect(badge.text()).toBe('shared.replayProvenance.label')
    expect(badge.attributes('title')).toBe(
      'shared.replayProvenance.tooltip:{"source":"exec-baseline","replayed":2,"eligible":3}',
    )
  })

  it('says coverage is pending while the frozen execution is still running', () => {
    const wrapper = mount(ReplayProvenanceBadge, {
      props: { freezeSourceExecutionId: 'exec-baseline', workflowState: WorkflowState.Running },
      global: { stubs },
    })
    expect(wrapper.find('.replay-provenance-badge').attributes('title')).toBe(
      'shared.replayProvenance.tooltipPending:{"source":"exec-baseline"}',
    )
  })

  it('says coverage was not recorded when the frozen execution ended without counts', () => {
    const wrapper = mount(ReplayProvenanceBadge, {
      props: { freezeSourceExecutionId: 'exec-baseline', workflowState: WorkflowState.Failed },
      global: { stubs },
    })
    expect(wrapper.find('.replay-provenance-badge').attributes('title')).toBe(
      'shared.replayProvenance.tooltipUnrecorded:{"source":"exec-baseline"}',
    )
  })
})
