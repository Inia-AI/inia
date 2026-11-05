import type { GridLayoutOutput } from '@/api/workflowReports/schema'

export function isGridLayout(layout?: GridLayoutOutput | null): boolean {
  return layout?.mode === 'grid'
}

export function gridContainerClass(layout?: GridLayoutOutput | null): string {
  if (!isGridLayout(layout)) {
    return ''
  }
  const columns = layout?.columns || 2
  return `grid gap-4 items-start grid-cols-1 md:grid-cols-${columns}`
}

export function gridItemClass(
  layout: GridLayoutOutput | null | undefined,
  paragraphOrder?: number,
): string {
  if (!isGridLayout(layout) || !layout?.elements) {
    return ''
  }

  const position = layout.elements.find(p => p.blockOrder === paragraphOrder)
  if (!position) {
    return ''
  }

  const { row, column, rowSpan, columnSpan, blockOrder } = position

  return [
    row ? `row-start-${blockOrder + 1} md:row-start-${row}` : '',
    column ? `col-start-1 md:col-start-${column}` : '',
    rowSpan ? `row-span-1 md:row-span-${rowSpan}` : '',
    columnSpan ? `col-span-1 md:col-span-${columnSpan}` : '',
  ]
    .filter(Boolean)
    .join(' ')
}
