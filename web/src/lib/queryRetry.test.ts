import { describe, expect, it } from 'vitest'
import { AxiosError, AxiosHeaders } from 'axios'
import { retryUnlessClientError } from './queryRetry'

const axiosErrorWithStatus = (status: number): AxiosError => {
  const error = new AxiosError('Request failed', 'ERR_BAD_REQUEST')
  error.response = {
    status,
    statusText: '',
    data: null,
    headers: {},
    config: { headers: new AxiosHeaders() },
  }
  return error
}

describe('retryUnlessClientError', () => {
  it('never retries a 4xx — the server understood the request and said no', () => {
    expect(retryUnlessClientError(0, axiosErrorWithStatus(404))).toBe(false)
    expect(retryUnlessClientError(0, axiosErrorWithStatus(400))).toBe(false)
    expect(retryUnlessClientError(0, axiosErrorWithStatus(403))).toBe(false)
  })

  it('retries a 5xx up to three times', () => {
    expect(retryUnlessClientError(0, axiosErrorWithStatus(500))).toBe(true)
    expect(retryUnlessClientError(2, axiosErrorWithStatus(500))).toBe(true)
    expect(retryUnlessClientError(3, axiosErrorWithStatus(500))).toBe(false)
  })

  it('retries a network fault with no response up to three times', () => {
    const networkError = new AxiosError('Network Error', 'ERR_NETWORK')
    expect(retryUnlessClientError(0, networkError)).toBe(true)
    expect(retryUnlessClientError(3, networkError)).toBe(false)
  })

  it('retries a non-axios error up to three times', () => {
    expect(retryUnlessClientError(0, new Error('boom'))).toBe(true)
    expect(retryUnlessClientError(3, new Error('boom'))).toBe(false)
  })
})
