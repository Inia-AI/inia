import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('@/components/ui/popover', async () =>
  (await import('@/test/helpers/rekaStubs')).popoverModule,
)

import DateRangeFilter from './DateRangeFilter.vue'
import { accessibleName } from '@/test/helpers/rowControls'

const PRESETS = [
  { label: 'Created Today', value: 'today' },
  { label: 'Created Last 7 Days', value: 'last7days' },
]

function mountFilter(modelValue: string) {
  return mount(DateRangeFilter, {
    props: {
      modelValue,
      presets: PRESETS,
      allLabel: 'All Created Dates',
      prefix: 'Created',
      clearLabel: 'Clear',
    },
    attachTo: document.body,
  })
}

const triggerText = (wrapper: ReturnType<typeof mountFilter>) =>
  wrapper.find('[data-stub="PopoverTrigger"] button').text()

describe('DateRangeFilter', () => {
  it('shows the all-dates label when nothing is picked', () => {
    const wrapper = mountFilter(' ')
    expect(triggerText(wrapper)).toBe('All Created Dates')
    wrapper.unmount()
  })

  it('shows the preset label for a preset value', () => {
    const wrapper = mountFilter('last7days')
    expect(triggerText(wrapper)).toBe('Created Last 7 Days')
    wrapper.unmount()
  })

  it('shows the span for a day range value', () => {
    const wrapper = mountFilter('2026-09-03..2026-09-09')
    expect(triggerText(wrapper)).toBe('Created 3–9 Sep 2026')
    wrapper.unmount()
  })

  it('emits the preset key when a preset is pressed', async () => {
    const wrapper = mountFilter(' ')
    await wrapper.find('[data-preset="today"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['today'])
    wrapper.unmount()
  })

  it('emits start..end once both ends of a span are picked on the calendar', async () => {
    const wrapper = mountFilter('2026-09-01..2026-09-01')
    await wrapper.find('[data-value="2026-09-03"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.find('[data-value="2026-09-09"]').trigger('focusin')
    await wrapper.find('[data-value="2026-09-09"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['2026-09-03..2026-09-09'])
    wrapper.unmount()
  })

  it('emits an empty value from Clear', async () => {
    const wrapper = mountFilter('2026-09-03..2026-09-09')
    const clear = wrapper.findAll('button').find(b => b.text() === 'Clear')
    expect(clear).toBeDefined()
    await clear!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([''])
    wrapper.unmount()
  })

  it('offers no Clear when nothing is picked', () => {
    const wrapper = mountFilter(' ')
    expect(wrapper.findAll('button').some(b => b.text() === 'Clear')).toBe(false)
    wrapper.unmount()
  })
})

describe('DateRangeFilter trigger name', () => {
  it('is named by what it filters followed by the label it shows', () => {
    const wrapper = mount(DateRangeFilter, {
      props: {
        modelValue: ' ',
        presets: PRESETS,
        allLabel: 'All Created Dates',
        prefix: 'Created',
        clearLabel: 'Clear',
        ariaLabel: 'Filter by created date',
      },
      attachTo: document.body,
    })

    const trigger = wrapper.get('[data-stub="PopoverTrigger"] button').element
    expect(trigger.getAttribute('aria-label')).toBeNull()
    expect(accessibleName(trigger)).toBe('Filter by created date All Created Dates')
    wrapper.unmount()
  })
})
