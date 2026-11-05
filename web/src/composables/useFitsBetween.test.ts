import { describe, expect, it } from 'vitest'
import { fitsBetween } from './useFitsBetween'

describe('fitsBetween', () => {
  const header = { start: 150, end: 330, middle: 650, clearance: 16 }

  it('fits a centred middle when it clears the wider side on both sides', () => {
    expect(fitsBetween({ ...header, available: 1342 })).toBe(true)
  })

  it('does not fit a centred middle that would run under the wider side', () => {
    expect(fitsBetween({ ...header, available: 1341 })).toBe(false)
  })

  it('measures both sides against the wider one, as the middle stays centred', () => {
    expect(fitsBetween({ ...header, start: 330, end: 150, available: 1341 })).toBe(false)
  })
})
