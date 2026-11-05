import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { axiosError } from '@/test/helpers/axiosError'
import { EN } from '@/test/helpers/messages'
import { dailyAllowanceMessage, dailyLimitFromPolicy, describeReset } from './dailyAllowance'

const { t } = createI18n({ legacy: false, locale: 'en', messages: { en: EN } }).global

const plainSpaces = (text: string | undefined) => text?.replace(/\s/g, ' ')

const RESETS_AT = '2026-11-04T00:00:00Z'
const AFTERNOON_UTC = new Date('2026-11-03T15:00:00Z')

const exhausted = (kind: 'explore' | 'refine', headers: Record<string, string> = {}) =>
  axiosError(
    429,
    {
      error: kind === 'explore'
        ? 'You have used your 7 daily explore questions. You can ask more from midnight UTC.'
        : 'You have used your 5 daily refine requests. You can refine again from midnight UTC.',
      code: 'DailyAllowance.Exhausted',
      resetsAt: RESETS_AT,
    },
    undefined,
    headers,
  )

describe('dailyLimitFromPolicy', () => {
  it('reads the quota of the policy', () => {
    expect(dailyLimitFromPolicy('"explore";q=7;w=86400')).toBe(7)
    expect(dailyLimitFromPolicy('"refine";w=86400;q=5')).toBe(5)
  })

  it('has no limit without a quota', () => {
    expect(dailyLimitFromPolicy(undefined)).toBeUndefined()
    expect(dailyLimitFromPolicy('"explore";w=86400')).toBeUndefined()
  })
})

describe('describeReset', () => {
  it('names midnight UTC tomorrow in London and this evening in New York', () => {
    expect(describeReset(new Date(RESETS_AT), { now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'Europe/London' }))
      .toEqual({ time: '00:00', isToday: false })
    const newYork = describeReset(new Date(RESETS_AT), { now: AFTERNOON_UTC, locale: 'en-US', timeZone: 'America/New_York' })
    expect({ ...newYork, time: plainSpaces(newYork.time) }).toEqual({ time: '7:00 PM', isToday: true })
  })

  it('names tomorrow in New York once its own day has turned', () => {
    const lateEvening = new Date('2026-11-04T04:30:00Z')
    const nextReset = new Date('2026-11-05T00:00:00Z')
    expect(describeReset(nextReset, { now: lateEvening, locale: 'en-US', timeZone: 'America/New_York' }).isToday).toBe(false)
  })

  it('follows the 24-hour clock of the locale', () => {
    expect(describeReset(new Date(RESETS_AT), { now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'America/New_York' }).time)
      .toBe('19:00')
  })
})

describe('dailyAllowanceMessage', () => {
  const policy = (kind: string, quota: number) => ({ 'ratelimit-policy': `"${kind}";q=${quota};w=86400` })

  it('says when explore opens again in the reader\'s own time', () => {
    expect(dailyAllowanceMessage(exhausted('explore', policy('explore', 7)), 'explore', t, {
      now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'Europe/London',
    })).toBe('You have used your 7 daily explore questions. You can ask more from 00:00 tomorrow.')
    expect(plainSpaces(dailyAllowanceMessage(exhausted('explore', policy('explore', 7)), 'explore', t, {
      now: AFTERNOON_UTC, locale: 'en-US', timeZone: 'America/New_York',
    }))).toBe('You have used your 7 daily explore questions. You can ask more from 7:00 PM today.')
  })

  it('says when refine opens again in the reader\'s own time', () => {
    expect(dailyAllowanceMessage(exhausted('refine', { 'RateLimit-Policy': '"refine";q=5;w=86400' }), 'refine', t, {
      now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'Europe/London',
    })).toBe('You have used your 5 daily refine requests. You can refine again from 00:00 tomorrow.')
  })

  it('shows the server text when the policy header is missing', () => {
    expect(dailyAllowanceMessage(exhausted('refine'), 'refine', t, { now: AFTERNOON_UTC }))
      .toBe('You have used your 5 daily refine requests. You can refine again from midnight UTC.')
  })

  it('counts a limit of one in the singular', () => {
    expect(dailyAllowanceMessage(exhausted('explore', policy('explore', 1)), 'explore', t, {
      now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'Europe/London',
    })).toBe('You have used your 1 daily explore question. You can ask more from 00:00 tomorrow.')
    expect(dailyAllowanceMessage(exhausted('refine', policy('refine', 1)), 'refine', t, {
      now: AFTERNOON_UTC, locale: 'en-GB', timeZone: 'Europe/London',
    })).toBe('You have used your 1 daily refine request. You can refine again from 00:00 tomorrow.')
  })

  it('leaves a refusal whose body does not parse to the generic text of the caller', () => {
    const withoutResetTime = axiosError(429, { error: 'You have used your 7 daily explore questions.', code: 'DailyAllowance.Exhausted' })
    const withABadResetTime = axiosError(429, { error: 'Used.', code: 'DailyAllowance.Exhausted', resetsAt: 'midnight' })
    const withANumberForText = axiosError(429, { error: 7, code: 'DailyAllowance.Exhausted', resetsAt: RESETS_AT })
    expect(dailyAllowanceMessage(withoutResetTime, 'explore', t)).toBeUndefined()
    expect(dailyAllowanceMessage(withABadResetTime, 'explore', t)).toBeUndefined()
    expect(dailyAllowanceMessage(withANumberForText, 'explore', t)).toBeUndefined()
    expect(dailyAllowanceMessage(axiosError(429, 'Too Many Requests'), 'explore', t)).toBeUndefined()
  })

  it('leaves other refusals to the caller', () => {
    expect(dailyAllowanceMessage(axiosError(429, { error: 'Slow down.' }), 'explore', t)).toBeUndefined()
    expect(dailyAllowanceMessage(axiosError(400, { code: 'DailyAllowance.Exhausted' }), 'explore', t)).toBeUndefined()
    expect(dailyAllowanceMessage(new Error('Network Error'), 'explore', t)).toBeUndefined()
  })
})
