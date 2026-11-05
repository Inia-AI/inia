import { isAxiosError } from 'axios'
import { HTTP_TOO_MANY_REQUESTS } from '@/api/base/errors'

export type SignInFailure = 'rateLimited' | 'lockedOut' | 'expired' | 'refused'

const ACCOUNT_LOCKED_OUT = 'account_locked_out'
// The code step and the email-code request name an expired second-factor step differently.
const EXPIRED_STEP = new Set(['invalid_mfa_token', 'user_not_found', 'MfaToken.Invalid'])

/**
 * Why the API turned a sign-in step away. The sign-in limit, a second-factor lockout and an
 * expired second-factor step need their own message: shown as a wrong password or a wrong code,
 * they send the user guessing.
 * The password step answers a lockout like a wrong password on purpose, so it never reads as one.
 */
export function signInFailure(error: unknown): SignInFailure {
  if (!isAxiosError(error)) {
    return 'refused'
  }

  if (error.response?.status === HTTP_TOO_MANY_REQUESTS) {
    return 'rateLimited'
  }

  const answer = error.response?.data as { error?: string; code?: string } | undefined
  if (answer?.error === ACCOUNT_LOCKED_OUT) {
    return 'lockedOut'
  }

  return EXPIRED_STEP.has(answer?.error ?? answer?.code ?? '') ? 'expired' : 'refused'
}

const TOO_MANY_ATTEMPTS_KEY = 'page.login.errors.tooManyAttempts'
const TOO_MANY_ATTEMPTS_MINUTES_KEY = 'page.login.errors.tooManyAttemptsMinutes'
const SECONDS_PER_MINUTE = 60

/** The whole minutes the API's Retry-After asks for, when it asks for more than one. */
function minutesToWait(error: unknown): number | null {
  const seconds = isAxiosError(error) ? Number(error.response?.headers?.['retry-after']) : Number.NaN
  return Number.isFinite(seconds) && seconds > SECONDS_PER_MINUTE ? Math.ceil(seconds / SECONDS_PER_MINUTE) : null
}

type Translate = (key: string, named?: Record<string, unknown>, plural?: number) => string

/** The sign-in limit's text, with the wait the API named when it is longer than a minute. */
export function tooManyAttemptsText(error: unknown, t: Translate): string {
  const minutes = minutesToWait(error)
  return minutes === null ? t(TOO_MANY_ATTEMPTS_KEY) : t(TOO_MANY_ATTEMPTS_MINUTES_KEY, { n: minutes }, minutes)
}

/** The text for a refused sign-in step: the API's own words when it gave them, else the text for its error code, else the fallback. */
export function signInRefusalText(
  error: unknown,
  t: Translate,
  keysByCode: Readonly<Record<string, string>>,
  fallbackKey: string,
): string {
  if (signInFailure(error) === 'rateLimited') {
    return tooManyAttemptsText(error, t)
  }
  const answer = isAxiosError(error) ? (error.response?.data as { error?: string; error_description?: string } | undefined) : undefined
  if (answer?.error_description) {
    return answer.error_description
  }
  return t(keysByCode[answer?.error ?? ''] ?? fallbackKey)
}
