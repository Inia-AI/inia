import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('@/components/ui/popover', async () =>
  (await import('@/test/helpers/rekaStubs')).popoverModule,
)

import MonthRangeFilter from './MonthRangeFilter.vue'

function mountFilter(modelValue: string) {
  return mount(MonthRangeFilter, {
    props: {
      modelValue,
      allLabel: 'All Reporting Dates',
      prefix: 'Reporting',
      clearLabel: 'Clear',
    },
    attachTo: document.body,
  })
}

const triggerText = (wrapper: ReturnType<typeof mountFilter>) =>
  wrapper.find('[data-stub="PopoverTrigger"] button').text()

describe('MonthRangeFilter', () => {
  it('shows the all-dates label when nothing is picked', () => {
    const wrapper = mountFilter(' ')
    expect(triggerText(wrapper)).toBe('All Reporting Dates')
    wrapper.unmount()
  })

  it('shows one month for the single-month value old links carry', () => {
    const wrapper = mountFilter('2026-03')
    expect(triggerText(wrapper)).toBe('Reporting Mar 2026')
    wrapper.unmount()
  })

  it('shows the span for a month range value', () => {
    const wrapper = mountFilter('2025-01..2025-06')
    expect(triggerText(wrapper)).toBe('Reporting Jan–Jun 2025')
    wrapper.unmount()
  })

  it('emits the single-month form when one month is picked twice', async () => {
    const wrapper = mountFilter('2025-03')
    await wrapper.find('[data-value="2025-06-01"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.find('[data-value="2025-06-01"]').trigger('focusin')
    await wrapper.find('[data-value="2025-06-01"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['2025-06'])
    wrapper.unmount()
  })

  it('emits start..end once both ends of a span are picked', async () => {
    const wrapper = mountFilter('2025-03')
    await wrapper.find('[data-value="2025-01-01"]').trigger('click')
    await wrapper.find('[data-value="2025-06-01"]').trigger('focusin')
    await wrapper.find('[data-value="2025-06-01"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['2025-01..2025-06'])
    wrapper.unmount()
  })

  it('emits an empty value from Clear', async () => {
    const wrapper = mountFilter('2025-01..2025-06')
    const clear = wrapper.findAll('button').find(b => b.text() === 'Clear')
    await clear!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([''])
    wrapper.unmount()
  })
})
