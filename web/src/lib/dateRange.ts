import { isValid, parseISO } from 'date-fns'

const RANGE_SEPARATOR = '..'

const DAY_PATTERN = /^\d{4}-\d{2}-\d{2}$/
const MONTH_PATTERN = /^\d{4}-\d{2}$/

export interface DayRange {
  start: string
  end: string
}

export interface MonthRange {
  start: string
  end: string
}

const isRealDay = (value: string): boolean =>
  DAY_PATTERN.test(value) && isValid(parseISO(value))

export const isRealMonth = (value: string): boolean =>
  MONTH_PATTERN.test(value) && isValid(parseISO(value))

const parseRange = (
  value: string,
  isReal: (part: string) => boolean,
): { start: string; end: string } | null => {
  const parts = value.split(RANGE_SEPARATOR)
  if (parts.length !== 2) return null
  const [start, end] = parts
  if (!isReal(start) || !isReal(end) || start > end) return null
  return { start, end }
}

export function parseDayRange(value: string): DayRange | null {
  return parseRange(value, isRealDay)
}

export function formatDayRange(range: DayRange): string {
  return `${range.start}${RANGE_SEPARATOR}${range.end}`
}

export function parseMonthRange(value: string): MonthRange | null {
  if (isRealMonth(value)) return { start: value, end: value }
  return parseRange(value, isRealMonth)
}

export function formatMonthRange(range: MonthRange): string {
  if (range.start === range.end) return range.start
  return `${range.start}${RANGE_SEPARATOR}${range.end}`
}

const dateParts = (value: string): [number, number, number] => {
  const [year, month, day = 1] = value.split('-').map(Number)
  return [year, month, day]
}

export function utcMidnight(day: string, daysAhead = 0): Date {
  const [year, month, dayOfMonth] = dateParts(day)
  return new Date(Date.UTC(year, month - 1, dayOfMonth + daysAhead))
}

export function utcFirstOfMonth(month: string, monthsAhead = 0): Date {
  const [year, monthOfYear] = dateParts(month)
  return new Date(Date.UTC(year, monthOfYear - 1 + monthsAhead, 1))
}
