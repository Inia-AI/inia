import { describe, expect, it } from 'vitest'
import { textColorProblems } from '@/test/helpers/colorUsages'

describe('text colours', () => {
  it('come from verified theme tokens, or clear WCAG AA on the ground set beside them, in both themes', () => {
    expect(textColorProblems()).toEqual([])
  })
})
