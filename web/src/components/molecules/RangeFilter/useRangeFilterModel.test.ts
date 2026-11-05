import { describe, expect, it } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { parseDate, toCalendarDate } from '@internationalized/date'
import { formatDayRange, parseDayRange } from '@/lib/dateRange'
import { useRangeFilterModel, type RangeUnit } from './useRangeFilterModel'

const DAY_UNIT: RangeUnit = {
  parse: parseDayRange,
  format: formatDayRange,
  toCalendar: parseDate,
  toValue: value => toCalendarDate(value).toString(),
  placeholderFor: picked => picked.start ?? parseDate('2026-09-01'),
}

function setup(initial: string) {
  const model = ref(initial)
  const scope = effectScope()
  let picker!: ReturnType<typeof useRangeFilterModel>
  scope.run(() => {
    picker = useRangeFilterModel(model, DAY_UNIT)
  })
  return { model, picker, scope }
}

describe('useRangeFilterModel', () => {
  it('commits only once both ends are picked, then closes', () => {
    const { model, picker, scope } = setup('')
    picker.open.value = true
    picker.onRangeUpdate({ start: parseDate('2026-09-03'), end: undefined })
    expect(model.value).toBe('')
    expect(picker.open.value).toBe(true)
    picker.onRangeUpdate({ start: parseDate('2026-09-03'), end: parseDate('2026-09-09') })
    expect(model.value).toBe('2026-09-03..2026-09-09')
    expect(picker.open.value).toBe(false)
    scope.stop()
  })

  it('drops a half-picked start and restores the committed span when reopened', async () => {
    const { picker, scope } = setup('2026-09-03..2026-09-09')
    picker.open.value = true
    await nextTick()
    picker.onRangeUpdate({ start: parseDate('2026-09-20'), end: undefined })
    expect(picker.selected.value.start?.toString()).toBe('2026-09-20')
    picker.open.value = false
    await nextTick()
    picker.open.value = true
    await nextTick()
    expect(picker.selected.value.start?.toString()).toBe('2026-09-03')
    expect(picker.selected.value.end?.toString()).toBe('2026-09-09')
    expect(picker.placeholder.value.toString()).toBe('2026-09-03')
    scope.stop()
  })

  it('follows an external model change while open', async () => {
    const { model, picker, scope } = setup('2026-09-03..2026-09-09')
    picker.open.value = true
    model.value = '2026-10-01..2026-10-02'
    await nextTick()
    expect(picker.selected.value.start?.toString()).toBe('2026-10-01')
    expect(picker.range.value).toEqual({ start: '2026-10-01', end: '2026-10-02' })
    scope.stop()
  })

  it('clears to an empty model and closes', async () => {
    const { model, picker, scope } = setup('2026-09-03..2026-09-09')
    picker.open.value = true
    picker.clear()
    expect(model.value).toBe('')
    expect(picker.open.value).toBe(false)
    await nextTick()
    expect(picker.selected.value.start).toBeUndefined()
    scope.stop()
  })
})
