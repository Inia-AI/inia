import { describe, it, expect } from 'vitest'
import type { GridLayoutOutput } from '@/api/workflowReports/schema'
import { isGridLayout, gridContainerClass, gridItemClass } from './reportLayout'

const gridLayout: GridLayoutOutput = {
  outputType: 'GridLayout',
  mode: 'grid',
  columns: 5,
  elements: [
    { blockOrder: 1, row: 1, column: 1, rowSpan: 1, columnSpan: 3 },
    { blockOrder: 2, row: 1, column: 4, rowSpan: 1, columnSpan: 2 },
  ],
}

describe('isGridLayout', () => {
  it('is true only when mode is grid', () => {
    expect(isGridLayout(gridLayout)).toBe(true)
    expect(isGridLayout({ mode: 'sequential' })).toBe(false)
    expect(isGridLayout(null)).toBe(false)
    expect(isGridLayout(undefined)).toBe(false)
  })
})

describe('gridContainerClass', () => {
  it('returns the column-bound grid container classes when grid', () => {
    expect(gridContainerClass(gridLayout)).toBe(
      'grid gap-4 items-start grid-cols-1 md:grid-cols-5',
    )
  })

  it('falls back to 2 columns when columns is missing', () => {
    expect(gridContainerClass({ mode: 'grid' })).toBe(
      'grid gap-4 items-start grid-cols-1 md:grid-cols-2',
    )
  })

  it('returns empty string for sequential or missing layout', () => {
    expect(gridContainerClass({ mode: 'sequential' })).toBe('')
    expect(gridContainerClass(null)).toBe('')
  })
})

describe('gridItemClass', () => {
  it('maps a matched paragraph order to row/column placement classes', () => {
    expect(gridItemClass(gridLayout, 2)).toBe(
      'row-start-3 md:row-start-1 col-start-1 md:col-start-4 row-span-1 md:row-span-1 col-span-1 md:col-span-2',
    )
  })

  it('returns empty string when the order has no grid position', () => {
    expect(gridItemClass(gridLayout, 99)).toBe('')
  })

  it('returns empty string when layout is not grid', () => {
    expect(gridItemClass({ mode: 'sequential' }, 1)).toBe('')
    expect(gridItemClass(null, 1)).toBe('')
  })
})
