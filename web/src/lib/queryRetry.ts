import { isAxiosError } from 'axios'

const MAX_RETRIES = 3

// A 4xx answer is deterministic — the server understood the request and refused it —
// so retrying can only delay the error state (a garbage-id page sat on its spinner
// for seven seconds before failing). Network faults and 5xx keep the default
// three attempts.
export function retryUnlessClientError(failureCount: number, error: unknown): boolean {
  const status = isAxiosError(error) ? error.response?.status : undefined
  if (status !== undefined && status >= 400 && status < 500) {
    return false
  }
  return failureCount < MAX_RETRIES
}
