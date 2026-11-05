import { format, isValid } from 'date-fns'

const DATE_VARIABLE_FORMAT = 'yyyy-MM-dd'
const LEADING_ISO_DAY = /^(\d{4}-\d{2}-\d{2})/

// A stored value that still has a time part keeps its written calendar day; parsing it as a Date
// would shift it through the browser's time zone.
export function toDateVariableValue(raw: unknown): string {
  if (typeof raw !== 'string' || raw === '') {
    return ''
  }

  const isoDay = LEADING_ISO_DAY.exec(raw)
  if (isoDay) {
    return isoDay[1]!
  }

  const parsed = new Date(raw)
  return isValid(parsed) ? format(parsed, DATE_VARIABLE_FORMAT) : ''
}
