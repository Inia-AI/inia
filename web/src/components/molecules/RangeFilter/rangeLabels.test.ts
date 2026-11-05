import { describe, expect, it } from 'vitest'
import { dayRangeLabel, monthRangeLabel } from './rangeLabels'

describe('dayRangeLabel', () => {
  it('collapses a single day', () => {
    expect(dayRangeLabel({ start: '2026-09-03', end: '2026-09-03' })).toBe('3 Sep 2026')
  })

  it('shares the month and year when both ends fall in one month', () => {
    expect(dayRangeLabel({ start: '2026-09-03', end: '2026-09-09' })).toBe('3–9 Sep 2026')
  })

  it('shares the year when both ends fall in one year', () => {
    expect(dayRangeLabel({ start: '2026-09-03', end: '2026-10-09' })).toBe('3 Sep – 9 Oct 2026')
  })

  it('spells both ends across a year boundary', () => {
    expect(dayRangeLabel({ start: '2025-12-30', end: '2026-01-02' })).toBe(
      '30 Dec 2025 – 2 Jan 2026',
    )
  })
})

describe('monthRangeLabel', () => {
  it('collapses a single month', () => {
    expect(monthRangeLabel({ start: '2026-03', end: '2026-03' })).toBe('Mar 2026')
  })

  it('shares the year when both ends fall in one year', () => {
    expect(monthRangeLabel({ start: '2025-01', end: '2025-06' })).toBe('Jan–Jun 2025')
  })

  it('spells both ends across a year boundary', () => {
    expect(monthRangeLabel({ start: '2025-11', end: '2026-02' })).toBe('Nov 2025 – Feb 2026')
  })
})
