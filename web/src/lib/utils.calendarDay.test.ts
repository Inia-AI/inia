process.env.TZ = 'America/New_York'

import { describe, expect, it } from 'vitest'

import { asCalendarDay, formatMonth } from './utils'

describe('calendar-bucket dates west of UTC', () => {
  it('formats a UTC-midnight month bucket as that month, not the evening before', () => {
    expect(formatMonth('2025-12-01T00:00:00Z')).toBe('Dec 2025')
    expect(formatMonth('2025-12-01T00:00:00')).toBe('Dec 2025')
  })

  it('reads a UTC-midnight day bucket as that calendar day', () => {
    const day = asCalendarDay('2026-09-17T00:00:00Z')
    expect([day.getFullYear(), day.getMonth() + 1, day.getDate()]).toEqual([2026, 9, 17])
  })
})
