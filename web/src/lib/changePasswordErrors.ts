import axios from 'axios'
import type { ProblemDetails } from '@/api/base/errors'
import { PASSWORD_MIN_LENGTH } from '@/lib/passwordPolicy'

const CURRENT_PASSWORD_ERROR_KEYS = new Set(['passwordmismatch', 'currentpassword'])

export type ChangePasswordErrorDescriber = (code: string, serverDescription: string) => string

export interface ChangePasswordErrors {
  currentPassword: string[]
  newPassword: string[]
}

const asServerText: ChangePasswordErrorDescriber = (_code, serverDescription) => serverDescription

export const PASSWORD_RULE_KEYS = 'page.profile.resetPassword.rules'

const LOCALIZED_IDENTITY_ERRORS: Record<string, string> = {
  passwordtooshort: `${PASSWORD_RULE_KEYS}.minLength`,
  passwordrequiresupper: `${PASSWORD_RULE_KEYS}.uppercase`,
  passwordrequireslower: `${PASSWORD_RULE_KEYS}.lowercase`,
  passwordrequiresdigit: `${PASSWORD_RULE_KEYS}.digit`,
  passwordmismatch: `${PASSWORD_RULE_KEYS}.currentPasswordIncorrect`,
}

/** Words Identity's password errors in the app's own rule texts, and keeps the server's text for the rest. */
export const localizedIdentityErrors =
  (t: (key: string, named: Record<string, unknown>) => string): ChangePasswordErrorDescriber =>
  (code, serverDescription) => {
    const key = LOCALIZED_IDENTITY_ERRORS[code.toLowerCase()]
    return key ? t(key, { minLength: PASSWORD_MIN_LENGTH }) : serverDescription
  }

const fieldKey = (code: string): string => code.replace(/^\$\./, '').toLowerCase()

export function changePasswordErrors(
  error: unknown,
  describe: ChangePasswordErrorDescriber = asServerText,
): ChangePasswordErrors {
  const result: ChangePasswordErrors = { currentPassword: [], newPassword: [] }
  if (!axios.isAxiosError(error)) {
    return result
  }

  const errors = (error.response?.data as ProblemDetails | undefined)?.errors
  if (!errors || typeof errors !== 'object') {
    return result
  }

  for (const [code, messages] of Object.entries(errors)) {
    const descriptions = (Array.isArray(messages) ? messages : [messages])
      .filter((message): message is string => typeof message === 'string' && message.trim().length > 0)
      .map(message => describe(code, message))
    const target = CURRENT_PASSWORD_ERROR_KEYS.has(fieldKey(code))
      ? result.currentPassword
      : result.newPassword
    target.push(...descriptions)
  }
  return result
}

export function changePasswordErrorMessages(errors: ChangePasswordErrors): string[] {
  return [...errors.currentPassword, ...errors.newPassword]
}
