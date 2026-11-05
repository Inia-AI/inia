import { format, parseISO } from 'date-fns'
import type { DayRange, MonthRange } from '@/lib/dateRange'

const EN_DASH = '–'

export function dayRangeLabel(range: DayRange): string {
  const end = parseISO(range.end)
  if (range.start === range.end) return format(end, 'd MMM yyyy')
  const start = parseISO(range.start)
  if (range.start.slice(0, 7) === range.end.slice(0, 7)) {
    return `${format(start, 'd')}${EN_DASH}${format(end, 'd MMM yyyy')}`
  }
  if (range.start.slice(0, 4) === range.end.slice(0, 4)) {
    return `${format(start, 'd MMM')} ${EN_DASH} ${format(end, 'd MMM yyyy')}`
  }
  return `${format(start, 'd MMM yyyy')} ${EN_DASH} ${format(end, 'd MMM yyyy')}`
}

export function monthRangeLabel(range: MonthRange): string {
  const end = parseISO(range.end)
  if (range.start === range.end) return format(end, 'MMM yyyy')
  const start = parseISO(range.start)
  if (range.start.slice(0, 4) === range.end.slice(0, 4)) {
    return `${format(start, 'MMM')}${EN_DASH}${format(end, 'MMM yyyy')}`
  }
  return `${format(start, 'MMM yyyy')} ${EN_DASH} ${format(end, 'MMM yyyy')}`
}
