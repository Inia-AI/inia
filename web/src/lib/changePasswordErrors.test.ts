import { describe, expect, it } from 'vitest'
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios'
import { changePasswordErrorMessages, changePasswordErrors } from './changePasswordErrors'

function makeAxiosError(status: number, data: unknown): AxiosError {
  const response: AxiosResponse = {
    data,
    status,
    statusText: 'Bad Request',
    headers: {},
    config: { headers: new AxiosHeaders() },
  }
  return new AxiosError(
    `Request failed with status code ${status}`,
    AxiosError.ERR_BAD_REQUEST,
    response.config,
    null,
    response,
  )
}

describe('changePasswordErrors', () => {
  it('routes the wrong-current-password code to the current password field', () => {
    const errors = changePasswordErrors(
      makeAxiosError(400, {
        title: 'One or more validation errors occurred.',
        errors: { PasswordMismatch: ['Incorrect password.'] },
      }),
    )
    expect(errors).toEqual({ currentPassword: ['Incorrect password.'], newPassword: [] })
  })

  it('routes every policy code to the new password field, in order', () => {
    const errors = changePasswordErrors(
      makeAxiosError(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          PasswordTooShort: ['Passwords must be at least 8 characters.'],
          PasswordRequiresUpper: ["Passwords must have at least one uppercase ('A'-'Z')."],
          PasswordRequiresDigit: ["Passwords must have at least one digit ('0'-'9')."],
        },
      }),
    )
    expect(errors.currentPassword).toEqual([])
    expect(errors.newPassword).toEqual([
      'Passwords must be at least 8 characters.',
      "Passwords must have at least one uppercase ('A'-'Z').",
      "Passwords must have at least one digit ('0'-'9').",
    ])
  })

  it('accepts a camelCased code and a bare string value', () => {
    const errors = changePasswordErrors(
      makeAxiosError(400, { errors: { passwordMismatch: 'Incorrect password.' } }),
    )
    expect(errors.currentPassword).toEqual(['Incorrect password.'])
  })

  it('routes a binding error on the current password field to that field', () => {
    const errors = changePasswordErrors(
      makeAxiosError(400, { errors: { '$.currentPassword': ['The JSON value could not be converted.'] } }),
    )
    expect(errors).toEqual({ currentPassword: ['The JSON value could not be converted.'], newPassword: [] })
  })

  it('lets the caller reword a known code and keeps the server text for the rest', () => {
    const errors = changePasswordErrors(
      makeAxiosError(400, {
        errors: {
          PasswordMismatch: ['Incorrect password.'],
          PasswordRequiresUpper: ["Passwords must have at least one uppercase ('A'-'Z')."],
          PasswordRequiresUniqueChars: ['Passwords must use at least 1 different characters.'],
        },
      }),
      (code, serverDescription) =>
        code === 'PasswordRequiresUpper' ? 'Needs an uppercase letter' : serverDescription,
    )
    expect(errors.currentPassword).toEqual(['Incorrect password.'])
    expect(errors.newPassword).toEqual([
      'Needs an uppercase letter',
      'Passwords must use at least 1 different characters.',
    ])
  })

  it('returns nothing for a problem without descriptions', () => {
    expect(changePasswordErrors(makeAxiosError(500, { title: 'An unhandled error occurred.' }))).toEqual({
      currentPassword: [],
      newPassword: [],
    })
    expect(changePasswordErrors(makeAxiosError(400, { errors: { PasswordTooShort: [''] } }))).toEqual({
      currentPassword: [],
      newPassword: [],
    })
  })

  it('returns nothing for a non-HTTP failure', () => {
    expect(changePasswordErrors(new Error('boom'))).toEqual({ currentPassword: [], newPassword: [] })
    expect(changePasswordErrors(undefined)).toEqual({ currentPassword: [], newPassword: [] })
  })
})

describe('changePasswordErrorMessages', () => {
  it('lists the current password message before the new password messages', () => {
    expect(
      changePasswordErrorMessages({
        currentPassword: ['Incorrect password.'],
        newPassword: ['Passwords must be at least 8 characters.'],
      }),
    ).toEqual(['Incorrect password.', 'Passwords must be at least 8 characters.'])
  })

  it('is empty when there is nothing to show', () => {
    expect(changePasswordErrorMessages({ currentPassword: [], newPassword: [] })).toEqual([])
  })
})
