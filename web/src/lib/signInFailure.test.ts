import { describe, expect, it } from 'vitest'
import { axiosError } from '@/test/helpers/axiosError'
import { signInFailure, signInRefusalText, tooManyAttemptsText } from './signInFailure'

describe('signInFailure', () => {
  it('reads a 429 as the sign-in limit', () => {
    expect(signInFailure(axiosError(429))).toBe('rateLimited')
  })

  it('reads the API lockout code as a lockout', () => {
    expect(signInFailure(axiosError(401, { error: 'account_locked_out' }))).toBe('lockedOut')
  })

  it('reads any other refusal as a plain refusal', () => {
    expect(signInFailure(axiosError(401, { error: 'invalid_code' }))).toBe('refused')
    expect(signInFailure(axiosError(403))).toBe('refused')
    expect(signInFailure(new Error('offline'))).toBe('refused')
  })

  it('reads an expired second-factor step as expired, whichever endpoint answered', () => {
    expect(signInFailure(axiosError(401, { error: 'invalid_mfa_token' }))).toBe('expired')
    expect(signInFailure(axiosError(401, { code: 'MfaToken.Invalid' }))).toBe('expired')
  })
})

describe('signInRefusalText', () => {
  const t = (key: string) => `text of ${key}`
  const KEYS = { sample_refusal: 'sample.refusal' }

  it('shows the API\'s own words first', () => {
    expect(signInRefusalText(axiosError(400, { error: 'sample_refusal', error_description: 'The API refused it.' }), t, KEYS, 'fallback')).toBe('The API refused it.')
  })

  it('names the text for the error code when the API gave no words', () => {
    expect(signInRefusalText(axiosError(400, { error: 'sample_refusal' }), t, KEYS, 'fallback')).toBe('text of sample.refusal')
  })

  it('says the network asked too often on a 429', () => {
    expect(signInRefusalText(axiosError(429), t, KEYS, 'fallback')).toBe('text of page.login.errors.tooManyAttempts')
  })

  it('falls back for an unknown code or an error that is not an API answer', () => {
    expect(signInRefusalText(axiosError(400, { error: 'other' }), t, KEYS, 'fallback')).toBe('text of fallback')
    expect(signInRefusalText(new Error('Request failed with status code 400'), t, KEYS, 'fallback')).toBe('text of fallback')
  })
})

describe('tooManyAttemptsText', () => {
  const t = (key: string, named?: Record<string, unknown>) => (named ? `${key} ${JSON.stringify(named)}` : key)

  it('names the minutes the API asks for when the wait is over a minute', () => {
    expect(tooManyAttemptsText(axiosError(429, {}, undefined, { 'retry-after': '3600' }), t)).toBe('page.login.errors.tooManyAttemptsMinutes {"n":60}')
    expect(tooManyAttemptsText(axiosError(429, {}, undefined, { 'retry-after': '61' }), t)).toBe('page.login.errors.tooManyAttemptsMinutes {"n":2}')
  })

  it('says a minute when the wait is a minute or less, or not given', () => {
    expect(tooManyAttemptsText(axiosError(429, {}, undefined, { 'retry-after': '60' }), t)).toBe('page.login.errors.tooManyAttempts')
    expect(tooManyAttemptsText(axiosError(429), t)).toBe('page.login.errors.tooManyAttempts')
  })
})
