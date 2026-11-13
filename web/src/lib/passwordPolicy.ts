import { z } from 'zod'

export const PASSWORD_MIN_LENGTH = 8

/** The server's length rule, checked before asking it. Only the server holds the list of commonly used passwords. */
export function newPasswordSchema(tooShort: string) {
  return z.string().min(PASSWORD_MIN_LENGTH, tooShort)
}
