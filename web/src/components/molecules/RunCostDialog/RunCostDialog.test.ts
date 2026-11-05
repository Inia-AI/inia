import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref, type Ref } from 'vue'

const { costQuery } = vi.hoisted(() => ({
  costQuery: {
    calls: [] as { executionId: Ref<string>; enabled: Ref<boolean> }[],
    data: null as unknown as Ref<unknown>,
  },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key) }),
}))

vi.mock('@/api', () => ({
  default: {
    TokenUsage: {
      useGetExecutionCost: (executionId: Ref<string>, enabled: Ref<boolean>) => {
        costQuery.calls.push({ executionId, enabled })
        return { data: costQuery.data, isLoading: ref(false), isError: ref(false) }
      },
    },
  },
}))

vi.mock('@/components/ui/dialog', async () => {
  const { defineComponent } = await import('vue')
  const passthrough = (name: string) => defineComponent({ name, template: '<div><slot /></div>' })
  return {
    Dialog: defineComponent({
      name: 'DialogStub',
      props: ['open'],
      template: '<div v-if="open"><slot /></div>',
    }),
    DialogContent: passthrough('DialogContent'),
    DialogDescription: passthrough('DialogDescription'),
    DialogHeader: passthrough('DialogHeader'),
    DialogTitle: passthrough('DialogTitle'),
  }
})

import RunCostDialog from './RunCostDialog.vue'

const line = (overrides: Record<string, unknown>) => ({
  blockId: 'block-1',
  name: 'Region summary',
  replacementTag: 'summary_N',
  blockType: 'Agent',
  models: ['claude-haiku-5-5'],
  calls: 2,
  inputTokens: 1200,
  outputTokens: 72,
  cacheReadTokens: 6229,
  cacheWriteTokens: 2987,
  cost: 0.0176368,
  ...overrides,
})

describe('RunCostDialog', () => {
  beforeEach(() => {
    costQuery.calls = []
    costQuery.data = ref({
      executionId: 'exec-1',
      totalCost: 0.0652,
      usageRecordCount: 5,
      unpricedRecordCount: 1,
      blocks: [line({}), line({ blockId: 'block-2', name: 'Collector', replacementTag: 'collector', cost: 0.0476 })],
      other: [line({ blockId: null, name: 'LlmOcrTranscription', replacementTag: null, cost: 0.0001 })],
    })
  })

  it('fetches the cost only while the dialog is open', () => {
    mount(RunCostDialog, { props: { open: false, executionId: 'exec-1' } })

    const call = costQuery.calls[0]
    expect(call.executionId.value).toBe('exec-1')
    expect(call.enabled.value).toBe(false)
  })

  it('shows the total, a line per block and the lines outside blocks', () => {
    const wrapper = mount(RunCostDialog, { props: { open: true, executionId: 'exec-1' } })

    expect(costQuery.calls[0].enabled.value).toBe(true)
    expect(wrapper.get('[data-testid="run-cost-total"]').text()).toBe('$0.0652')
    const blockRows = wrapper.get('[data-testid="run-cost-blocks"]').findAll('tbody tr')
    expect(blockRows).toHaveLength(2)
    expect(blockRows[0].text()).toContain('Region summary')
    expect(blockRows[0].text()).toContain('(summary_N)')
    expect(blockRows[0].text()).toContain('claude-haiku-5-5')
    expect(blockRows[0].text()).toContain('1,200')
    expect(blockRows[0].text()).toContain('6,229 / 2,987')
    expect(blockRows[0].text()).toContain('$0.0176')
    expect(wrapper.get('[data-testid="run-cost-other"]').text()).toContain('LlmOcrTranscription')
    expect(wrapper.get('[data-testid="run-cost-unpriced"]').text()).toContain('"count":1')
  })

  it('renders nothing while closed', () => {
    const wrapper = mount(RunCostDialog, { props: { open: false, executionId: 'exec-1' } })

    expect(wrapper.find('[data-testid="run-cost-total"]').exists()).toBe(false)
  })
})
