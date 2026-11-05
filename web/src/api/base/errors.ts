import axios from 'axios'

export const HTTP_FORBIDDEN = 403
export const HTTP_TOO_MANY_REQUESTS = 429

export interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
  capability?: string
}

// Placeholder titles ASP.NET puts on ValidationProblem/500 responses; the real
// message lives in `errors` (validation) or nowhere (unhandled), so these must
// not displace the localized fallback. Compared without trailing punctuation to
// survive wording drift between framework versions.
const GENERIC_TITLES = new Set([
  'one or more validation errors occurred',
  'an unhandled error occurred',
])

function isGenericTitle(title: string): boolean {
  return GENERIC_TITLES.has(title.trim().replace(/\.$/, '').toLowerCase())
}

function firstValidationMessage(errors?: Record<string, string[]>): string | undefined {
  if (!errors) return undefined
  for (const messages of Object.values(errors)) {
    if (messages?.length) return messages[0]
  }
  return undefined
}

/**
 * Extracts a human-readable message from an API error response.
 * The backend returns RFC 9110 ProblemDetails: conflict responses carry the
 * actionable message in `title` (e.g. duplicate-counterparty conflicts), while
 * validation responses carry field messages in `errors` under a generic title.
 * Falls back to the provided message when no meaningful server text exists.
 */
export function extractApiErrorMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error)) {
    return problemDetailsMessage(error.response?.data as ProblemDetails | undefined) ?? fallback
  }
  return fallback
}

/**
 * The message pick alone, for callers that already hold a parsed ProblemDetails body
 * (e.g. one read back out of a blob-typed error response).
 */
export function problemDetailsMessage(data: ProblemDetails | undefined): string | undefined {
  const validationMessage = firstValidationMessage(data?.errors)
  if (validationMessage) return validationMessage
  if (data?.capability && data.detail) return data.detail
  if (data?.title && !isGenericTitle(data.title)) return data.title
  return data?.detail || undefined
}
