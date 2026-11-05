import { describe, expect, it } from 'vitest'
import { toDateVariableValue } from './dateVariable'

describe('toDateVariableValue', () => {
  it.each([
    ['2026-09-25', '2026-09-25'],
    ['2026-09-25T00:00', '2026-09-25'],
    ['2026-09-25T23:30', '2026-09-25'],
    ['2026-09-25T00:00:00Z', '2026-09-25'],
  ])('keeps the calendar day of %j', (raw, expected) => {
    expect(toDateVariableValue(raw)).toBe(expected)
  })

  it('formats a parseable date written another way as yyyy-MM-dd', () => {
    expect(toDateVariableValue('September 25, 2026')).toBe('2026-09-25')
  })

  it.each([[''], ['not a date'], [undefined], [null], [42]])('gives an empty value for %j', raw => {
    expect(toDateVariableValue(raw)).toBe('')
  })
})
