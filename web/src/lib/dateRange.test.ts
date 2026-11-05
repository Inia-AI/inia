import { describe, expect, it } from 'vitest'
import {
  formatDayRange,
  formatMonthRange,
  parseDayRange,
  parseMonthRange,
} from './dateRange'

describe('parseDayRange', () => {
  it('reads a start..end pair of ISO days', () => {
    expect(parseDayRange('2026-09-03..2026-09-09')).toEqual({
      start: '2026-09-03',
      end: '2026-09-09',
    })
  })

  it('accepts a single-day span', () => {
    expect(parseDayRange('2026-09-03..2026-09-03')).toEqual({
      start: '2026-09-03',
      end: '2026-09-03',
    })
  })

  it('rejects a preset key, a lone day, a reversed span and an impossible day', () => {
    expect(parseDayRange('last7days')).toBeNull()
    expect(parseDayRange('2026-09-03')).toBeNull()
    expect(parseDayRange('2026-09-09..2026-09-03')).toBeNull()
    expect(parseDayRange('2026-02-30..2026-03-01')).toBeNull()
    expect(parseDayRange('2026-9-3..2026-9-9')).toBeNull()
    expect(parseDayRange('')).toBeNull()
  })
})

describe('formatDayRange', () => {
  it('round-trips through parseDayRange', () => {
    const range = { start: '2025-12-31', end: '2026-01-02' }
    expect(parseDayRange(formatDayRange(range))).toEqual(range)
  })
})

describe('parseMonthRange', () => {
  it('reads a single month as a span of one', () => {
    expect(parseMonthRange('2026-03')).toEqual({ start: '2026-03', end: '2026-03' })
  })

  it('reads a start..end pair of months', () => {
    expect(parseMonthRange('2025-01..2025-06')).toEqual({
      start: '2025-01',
      end: '2025-06',
    })
  })

  it('rejects a malformed month, a reversed span and a day value', () => {
    expect(parseMonthRange('2026-3')).toBeNull()
    expect(parseMonthRange('March')).toBeNull()
    expect(parseMonthRange('2026-13')).toBeNull()
    expect(parseMonthRange('2025-06..2025-01')).toBeNull()
    expect(parseMonthRange('2026-09-03..2026-09-09')).toBeNull()
    expect(parseMonthRange('')).toBeNull()
  })
})

describe('formatMonthRange', () => {
  it('writes a span of one month in the single-month form so old links stay valid', () => {
    expect(formatMonthRange({ start: '2026-03', end: '2026-03' })).toBe('2026-03')
  })

  it('round-trips a multi-month span', () => {
    const range = { start: '2025-11', end: '2026-02' }
    expect(formatMonthRange(range)).toBe('2025-11..2026-02')
    expect(parseMonthRange(formatMonthRange(range))).toEqual(range)
  })
})
