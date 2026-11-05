import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'
import { format, formatDistanceToNowStrict } from 'date-fns'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

// Server timestamps are UTC, but a SQL Server deployment serializes them without a zone
// marker, because its `datetime2` carries no offset. Parsed as local time, a run that just
// finished would read as hours in the future. Every formatter below goes through this.
const asUtcInstant = (dateString: string): string =>
  dateString.includes('T') && !/(Z|[+-]\d{2}:?\d{2})$/.test(dateString)
    ? `${dateString}Z`
    : dateString

export const parseServerDate = (value: string | Date): Date =>
  value instanceof Date ? value : new Date(asUtcInstant(value))

// A summary month, a reporting month, a test date: the server pins these at UTC midnight and
// means the calendar day, so a viewer west of UTC must not see the evening before.
export const asCalendarDay = (value: string): Date => {
  const instant = parseServerDate(value)
  return new Date(instant.getUTCFullYear(), instant.getUTCMonth(), instant.getUTCDate())
}

/**
 * Format a date string to dd/MM/yyyy format
 * Returns '-' if the date is null/undefined or invalid
 */
export const formatDate = (dateString: string | null | undefined): string => {
  if (!dateString) return '-'
  try {
    return format(parseServerDate(dateString), 'dd/MM/yyyy')
  } catch {
    return '-'
  }
}

const formatServerDate = (dateString: string | null | undefined, pattern: string): string => {
  if (!dateString) return '-'
  try {
    return format(parseServerDate(dateString), pattern)
  } catch {
    return '-'
  }
}

/**
 * Format a date string to dd/MM/yyyy HH:mm format
 * Returns '-' if the date is null/undefined or invalid
 */
export const formatDateTime = (dateString: string | null | undefined): string =>
  formatServerDate(dateString, 'dd/MM/yyyy HH:mm')

/** Day, month name and year, such as 5 December 2026. */
export const formatLongDate = (dateString: string | null | undefined): string => formatServerDate(dateString, 'd MMMM yyyy')

export const formatDateTimeSeconds = (dateString: string | null | undefined): string =>
  formatServerDate(dateString, 'dd/MM/yyyy HH:mm:ss')

/**
 * Format a date string to HH:mm format (24h, e.g. "16:38")
 * Returns '-' if the date is null/undefined or invalid
 */
export const formatTime = (dateString: string | null | undefined): string => {
  if (!dateString) return '-'
  try {
    return format(parseServerDate(dateString), 'HH:mm')
  } catch {
    return '-'
  }
}

/**
 * Format a date string to MMM yyyy format (e.g., "Jan 2024")
 * Returns '-' if the date is null/undefined or invalid
 */
export const formatMonth = (dateString: string | null | undefined): string => {
  if (!dateString) return '-'
  try {
    return format(asCalendarDay(dateString), 'MMM yyyy')
  } catch {
    return '-'
  }
}

/**
 * Format a date string as a relative age (e.g., "3 minutes ago")
 * Returns null if the date is null/undefined or invalid
 */
export const formatRelativeToNow = (dateString: string | null | undefined): string | null => {
  if (!dateString) return null
  try {
    return formatDistanceToNowStrict(parseServerDate(dateString), { addSuffix: true })
  } catch {
    return null
  }
}

/**
 * Parse a .NET TimeSpan string (e.g. "00:01:30", "1.02:00:00", "00:00:01.5000000")
 * into total milliseconds. Returns null when the input cannot be parsed.
 */
const parseDotNetTimeSpan = (raw: string): number | null => {
  // Optional leading "d." segment, then hh:mm:ss[.fffffff]
  const match = raw.match(
    /^(?:(\d+)\.)?(\d{1,2}):(\d{1,2}):(\d{1,2})(?:\.(\d{1,7}))?$/,
  )
  if (!match) return null

  const [, daysStr, hoursStr, minutesStr, secondsStr, fractionStr] = match
  const days = daysStr ? Number(daysStr) : 0
  const hours = Number(hoursStr)
  const minutes = Number(minutesStr)
  const seconds = Number(secondsStr)
  // Fractional seconds: pad to 7 digits (TimeSpan ticks), then convert to ms.
  const fractionMs = fractionStr
    ? Number(fractionStr.padEnd(7, '0')) / 10_000
    : 0

  if (
    [days, hours, minutes, seconds].some(n => Number.isNaN(n)) ||
    Number.isNaN(fractionMs)
  ) {
    return null
  }

  return (
    days * 86_400_000 +
    hours * 3_600_000 +
    minutes * 60_000 +
    seconds * 1_000 +
    fractionMs
  )
}

/**
 * Format raw milliseconds into a compact, user-friendly form: "500ms", "1.5s",
 * "2m 30s", "1h 30m", "1d 5h". Negative input is clamped to 0.
 */
export const formatDurationMs = (totalMs: number): string => {
  // Each branch rounds to its own resolution and falls through if rounding
  // pushes the value past the upper bound (e.g. 999.5ms → 1000ms is shown
  // as 1s, 59.5s → 60s is shown as 1m 0s).
  const safeMs = Math.max(0, totalMs)

  const ms = Math.round(safeMs)
  if (ms < 1_000) {
    return `${ms}ms`
  }

  // Whole seconds: a run's elapsed time is read at a glance, and tenths are
  // noise at that resolution. Rounding to 60 falls through to the minute
  // branch below, so 59.5s reads as "1m 0s" rather than "60s".
  const wholeSeconds = Math.round(safeMs / 1_000)
  if (wholeSeconds < 60) {
    return `${wholeSeconds}s`
  }

  const totalSeconds = Math.round(safeMs / 1_000)
  const totalMinutes = Math.floor(totalSeconds / 60)
  if (totalMinutes < 60) {
    const seconds = totalSeconds - totalMinutes * 60
    return `${totalMinutes}m ${seconds}s`
  }

  const totalHours = Math.floor(totalMinutes / 60)
  if (totalHours < 24) {
    const minutes = totalMinutes - totalHours * 60
    return `${totalHours}h ${minutes}m`
  }

  const totalDays = Math.floor(totalHours / 24)
  const hours = totalHours - totalDays * 24
  return `${totalDays}d ${hours}h`
}

/**
 * Format a .NET TimeSpan duration string into a compact, user-friendly form:
 * "500ms", "1.5s", "2m 30s", "1h 30m", "1d 5h". Returns '-' for nullish or
 * unparseable input.
 */
export const formatDuration = (value: string | null | undefined): string => {
  if (!value) return '-'

  const totalMs = parseDotNetTimeSpan(value)
  if (totalMs === null) return '-'

  return formatDurationMs(totalMs)
}

export const getDecodedToken = () => {
  const accessToken = localStorage.getItem('accessToken')

  if (!accessToken) {
    return null
  }

  const [, payload] = accessToken?.split('.') || []
  if (!payload) {
    return null
  }
  return JSON.parse(atob(payload))
}

/** Keeps the refresh token a sign-in or refresh returned. The token that ends a session comes without one. */
export const storeRefreshToken = (refreshToken: string | undefined) => {
  if (refreshToken) {
    localStorage.setItem('refreshToken', refreshToken)
  } else {
    localStorage.removeItem('refreshToken')
  }
}

export const clearSignIn = () => {
  localStorage.removeItem('accessToken')
  localStorage.removeItem('refreshToken')
  localStorage.removeItem('expiration')
  // The next user on this browser must not send this user's tenant header.
  localStorage.removeItem('selectedOrgId')
}

export const hasOrganization = (): boolean => Boolean(getDecodedToken()?.organization_id)

export const activeOrganizationId = (): string | undefined =>
  localStorage.getItem('selectedOrgId') || getDecodedToken()?.organization_id

type UserIdClaims = { sub?: unknown; user_id?: unknown; userId?: unknown; id?: unknown } | null | undefined

export const tokenUserId = (decoded: UserIdClaims): string | null => {
  const candidate = decoded?.sub ?? decoded?.user_id ?? decoded?.userId ?? decoded?.id
  return typeof candidate === 'string' ? candidate : null
}

export const ownEmail = (): { userId: string | null; email: string | undefined } => {
  const decoded = getDecodedToken()
  return { userId: tokenUserId(decoded), email: decoded?.email }
}

export const isTokenValid = () => {
  const decoded = getDecodedToken()
  const currentTime = Math.floor(Date.now() / 1000)

  return decoded?.exp > currentTime
}

/**
 * Checks if the currently decoded JWT token contains the specified role.
 * Handles both string and array representations of roles in the token payload.
 *
 * @param {string} role - The role to check for in the decoded JWT token.
 * @returns {boolean} True if the role exists in the token, false otherwise.
 */
export const hasRole = (role: string): boolean => {
  const decoded = getDecodedToken()

  if (!decoded) {
    return false
  }

  // JWT roles can be a string or an array - use optional chaining for safety
  const roles = decoded?.role

  if (!roles) {
    return false
  }

  // If roles is an array, check if the role exists in it
  if (Array.isArray(roles)) {
    return roles.includes(role)
  }

  // If roles is a string, check for exact match
  return roles === role
}

export const hasAnyRole = (roles: readonly string[]): boolean => roles.some(role => hasRole(role))

export const hasMultipleTenants = (): boolean => {
  const decoded = getDecodedToken()

  if (!decoded) {
    return false
  }

  // Check tenant_ids claim which contains the list of organizations - use optional chaining for safety
  const tenantIds = decoded?.tenant_ids

  if (!tenantIds) {
    return false
  }

  // tenant_ids is a JSON array string, parse it
  try {
    const tenants = JSON.parse(tenantIds)
    return Array.isArray(tenants) && tenants.length > 1
  } catch {
    return false
  }
}

/**
 * Get CSS class for observation status based on error/warning messages. A hedge fund
 * observation says whether its open warning needs review; any other reads its warning.
 */
export const getObservationStatusClass = (obs: {
  errorMessage: string | null
  warningMessage: string | null
  warningNeedsReview?: boolean
}): 'status-error' | 'status-warning' | 'status-ok' => {
  if (obs.errorMessage) return 'status-error'
  if (obs.warningNeedsReview ?? obs.warningMessage) return 'status-warning'
  return 'status-ok'
}

/**
 * Check if an observation has error, warning, or cleared warning messages
 */
export const hasObservationMessages = (obs: {
  errorMessage: string | null
  warningMessage: string | null
  clearedWarningMessage: string | null
}): boolean => {
  return Boolean(obs.errorMessage || obs.warningMessage || obs.clearedWarningMessage)
}

const XML_TAG_RE = /<\/?[a-zA-Z!][a-zA-Z0-9_:-]*(?:\s+[^<>\s][^<>]*?)?\s*>/g

/**
 * Strip XML-ish tags the LLM emits as structural wrappers (<answer>,
 * <thinking>, <reasoning>, etc.), preserving inner content.
 *
 * Pattern requirements baked into XML_TAG_RE:
 *   - `<` must be immediately followed by an alpha / `/` / `!` (a real tag
 *     opener) — rules out prose like `x < 5` or `foo < bar > baz` where
 *     `<` is followed by whitespace.
 *   - Tag name can only contain letters, digits, `-`, `_`, `:`.
 *   - Attribute area (when present) disallows `<` and `>` so matches don't
 *     span unrelated punctuation.
 *   - Trailing whitespace before the closing `>` is allowed, so adversarial
 *     forms like `<script \n>` (CodeQL js/bad-tag-filter) cannot slip past.
 *
 * Replacement loops until the output is stable so adversarial nesting like
 * `<scr<script>x</script>ipt>` cannot leave a residual tag behind after a
 * single pass.
 */
export const stripXmlTags = (text: string): string => {
  let previous: string
  let current = text
  do {
    previous = current
    current = current.replace(XML_TAG_RE, '')
  } while (current !== previous)
  return current
}
