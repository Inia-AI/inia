import axios from 'axios'
import { resetTimeAndDay, type ResetClock, type Translate } from '@/api/base/dailyAllowance'
import { HTTP_FORBIDDEN, HTTP_TOO_MANY_REQUESTS } from '@/api/base/errors'
import { StorageFullRefusal, TooManyUploadsRefusal } from './schema'

/**
 * The message for an upload the organization's limits refused: the platform's own words for full
 * storage, and for too many uploads in an hour the time one more fits, in the reader's own time zone.
 * Undefined for any other failure.
 */
export function uploadLimitMessage(error: unknown, t: Translate, clock: ResetClock = {}): string | undefined {
  if (!axios.isAxiosError(error)) {
    return undefined
  }
  const { status, data } = error.response ?? {}
  if (status === HTTP_FORBIDDEN) {
    return StorageFullRefusal.safeParse(data).data?.title
  }
  if (status !== HTTP_TOO_MANY_REQUESTS) {
    return undefined
  }
  const refusal = TooManyUploadsRefusal.safeParse(data)
  if (!refusal.success) {
    return undefined
  }
  return t('shared.components.fileControl.tooManyUploads', resetTimeAndDay(new Date(refusal.data.resetsAt), t, clock))
}
