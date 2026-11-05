import { z } from 'zod'

export const PASSWORD_MIN_LENGTH = 8

export interface PasswordRuleMessages {
  minLength: string
  uppercase: string
  lowercase: string
  digit: string
}

export function newPasswordSchema(messages: PasswordRuleMessages) {
  return z
    .string()
    .min(PASSWORD_MIN_LENGTH, messages.minLength)
    .regex(/[A-Z]/, messages.uppercase)
    .regex(/[a-z]/, messages.lowercase)
    .regex(/[0-9]/, messages.digit)
}
