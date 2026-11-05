import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { axiosError } from '@/test/helpers/axiosError'
import { describeReset } from '@/api/base/dailyAllowance'

const queryProvenance = vi.hoisted(() => vi.fn())

vi.mock('@/api', () => ({
  default: { GraphRag: { useQueryProvenance: () => ({ mutateAsync: queryProvenance }) } },
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

vi.mock('@/components/ui/dialog', async () => {
  const { defineComponent } = await import('vue')
  const passthrough = (name: string) => defineComponent({ name, template: '<div><slot /></div>' })
  return {
    Dialog: defineComponent({ name: 'DialogStub', props: ['open'], template: '<div v-if="open"><slot /></div>' }),
    DialogContent: passthrough('DialogContent'),
    DialogDescription: passthrough('DialogDescription'),
    DialogHeader: passthrough('DialogHeader'),
    DialogTitle: passthrough('DialogTitle'),
  }
})

import TracerDialog from './TracerDialog.vue'

const RESETS_AT = '2026-11-04T00:00:00Z'
const NOW = new Date('2026-11-03T15:00:00Z')

const mountOpen = async () => {
  const wrapper = mount(TracerDialog, {
    props: { runId: 'run-1', targetTag: 'tag', isOpen: false, initialQuestion: 'Where does this figure come from?' },
    global: { stubs: { MarkdownText: true } },
  })
  await wrapper.setProps({ isOpen: true })
  return wrapper
}

describe('TracerDialog', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('tells the reader when explore opens again once the daily questions are used', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
    queryProvenance.mockRejectedValue(axiosError(
      429,
      { error: 'Server text.', code: 'DailyAllowance.Exhausted', resetsAt: RESETS_AT },
      undefined,
      { 'ratelimit-policy': '"explore";q=7;w=86400' },
    ))
    const wrapper = await mountOpen()

    await wrapper.get('button.submit-button').trigger('click')
    await flushPromises()

    const { time, isToday } = describeReset(new Date(RESETS_AT), { now: NOW })
    const day = isToday ? 'shared.dailyAllowance.today' : 'shared.dailyAllowance.tomorrow'
    expect(wrapper.get('.error-text').text()).toBe(`shared.dailyAllowance.explore:${JSON.stringify({ count: 7, time, day })}`)
    expect(wrapper.get('textarea').attributes('disabled')).toBeUndefined()
  })

  it('keeps showing the server text for other refusals', async () => {
    queryProvenance.mockRejectedValue(axiosError(500, { error: 'The analysis service is unavailable.' }))
    const wrapper = await mountOpen()

    await wrapper.get('button.submit-button').trigger('click')
    await flushPromises()

    expect(wrapper.get('.error-text').text()).toBe('The analysis service is unavailable.')
  })
})
