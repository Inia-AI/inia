import axios, { AxiosHeaders } from 'axios'
import { z } from 'zod'
import { HTTP_TOO_MANY_REQUESTS } from '@/api/base/errors'

export type DailyAllowanceKind = 'explore' | 'refine' | 'helpRequest' | 'helpRequestReply'

export interface ResetClock {
  now?: Date
  locale?: string
  timeZone?: string
}

export type Translate = (key: string, named?: Record<string, unknown>, plural?: number) => string

const EXHAUSTED_CODE = 'DailyAllowance.Exhausted'

const DailyAllowanceRefusal = z.object({
  error: z.string(),
  code: z.literal(EXHAUSTED_CODE),
  resetsAt: z.iso.datetime(),
})
const POLICY_HEADER = 'ratelimit-policy'
const POLICY_QUOTA = /;\s*q=(\d+)/

export function dailyLimitFromPolicy(policy: string | undefined): number | undefined {
  const quota = policy?.match(POLICY_QUOTA)?.[1]
  return quota === undefined ? undefined : Number(quota)
}

function calendarDay(instant: Date, timeZone: string | undefined): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(instant)
}

export function describeReset(resetsAt: Date, clock: ResetClock = {}): { time: string; isToday: boolean } {
  const time = new Intl.DateTimeFormat(clock.locale, { timeStyle: 'short', timeZone: clock.timeZone }).format(resetsAt)
  const isToday = calendarDay(resetsAt, clock.timeZone) === calendarDay(clock.now ?? new Date(), clock.timeZone)
  return { time, isToday }
}

/** The time a limit resets in the reader's clock, and whether that is today or tomorrow, for a refusal message. */
export function resetTimeAndDay(resetsAt: Date, t: Translate, clock: ResetClock = {}): { time: string; day: string } {
  const { time, isToday } = describeReset(resetsAt, clock)
  return { time, day: t(isToday ? 'shared.dailyAllowance.today' : 'shared.dailyAllowance.tomorrow') }
}

export function dailyAllowanceMessage(
  error: unknown,
  kind: DailyAllowanceKind,
  t: Translate,
  clock: ResetClock = {},
): string | undefined {
  if (!axios.isAxiosError(error) || error.response?.status !== HTTP_TOO_MANY_REQUESTS) {
    return undefined
  }
  const parsed = DailyAllowanceRefusal.safeParse(error.response.data)
  if (!parsed.success) {
    return undefined
  }
  const body = parsed.data
  const policy = AxiosHeaders.from(error.response.headers as AxiosHeaders).get(POLICY_HEADER)
  const count = dailyLimitFromPolicy(typeof policy === 'string' ? policy : undefined)
  if (count === undefined) {
    return body.error
  }
  return t(`shared.dailyAllowance.${kind}`, { count, ...resetTimeAndDay(new Date(body.resetsAt), t, clock) }, count)
}
