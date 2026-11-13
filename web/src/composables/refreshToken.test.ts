import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { makeJwt } from '@/test/helpers/jwt'
import { axiosError } from '@/test/helpers/axiosError'

const { refresh, push, currentRoute } = vi.hoisted(() => ({
  refresh: vi.fn(),
  push: vi.fn(),
  currentRoute: { value: { fullPath: '/reports/42', meta: {} as { isPublic?: boolean } } },
}))

vi.mock('@/api', () => ({
  default: { Authentication: { useRefreshToken: () => ({ mutateAsync: refresh }) } },
}))

vi.mock('@/plugins/router', () => ({ default: { push, currentRoute } }))

import { useRefreshToken } from './refreshToken'

const tokenFor = (seconds: number) => makeJwt({ exp: Math.floor(Date.now() / 1000) + seconds })

const signInFor = (seconds: number) => {
  localStorage.setItem('accessToken', tokenFor(seconds))
  localStorage.setItem('refreshToken', 'refresh-1')
  localStorage.setItem('expiration', (Date.now() + seconds * 1000).toString())
}

describe('useRefreshToken', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    refresh.mockReset()
    push.mockReset()
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('refreshes a token one minute before it expires', async () => {
    signInFor(15 * 60)
    refresh.mockResolvedValue({ access_token: tokenFor(15 * 60), refresh_token: 'refresh-2', expires_in: 15 * 60 })

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(14 * 60 * 1000)

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(refresh).toHaveBeenCalledWith('refresh-1')
  })

  it('refreshes at once a token with under a minute left', async () => {
    signInFor(30)
    refresh.mockResolvedValue({ access_token: tokenFor(15 * 60), refresh_token: 'refresh-2', expires_in: 15 * 60 })

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(0)

    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('stops refreshing when the session ends with a token that brings no refresh token', async () => {
    signInFor(90)
    refresh.mockImplementation(async () => {
      localStorage.setItem('accessToken', tokenFor(60))
      return { access_token: tokenFor(60), expires_in: 60 }
    })

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(5 * 60 * 1000)

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(localStorage.getItem('refreshToken')).toBeNull()
    expect(push).toHaveBeenCalledWith({ name: 'Login', query: { reason: 'session-ended', redirect: '/reports/42' } })
  })

  it('waits for the last token of a session to expire before saying the session ended', async () => {
    localStorage.setItem('accessToken', tokenFor(10 * 60))
    localStorage.setItem('expiration', (Date.now() + 10 * 60 * 1000).toString())

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(10 * 60 * 1000 - 1)
    expect(push).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)
    expect(refresh).not.toHaveBeenCalled()
    expect(localStorage.getItem('accessToken')).toBeNull()
    expect(push).toHaveBeenCalledWith({ name: 'Login', query: { reason: 'session-ended', redirect: '/reports/42' } })
  })

  it('leaves the sign-in screen alone when the user signed out before the last token expired', async () => {
    localStorage.setItem('accessToken', tokenFor(10 * 60))
    localStorage.setItem('expiration', (Date.now() + 10 * 60 * 1000).toString())

    await useRefreshToken().setRefreshTokenTimer()
    localStorage.clear()
    await vi.advanceTimersByTimeAsync(10 * 60 * 1000)

    expect(push).not.toHaveBeenCalled()
  })

  it('never writes the refresh token to the console when a refresh fails', async () => {
    signInFor(30)
    const failure = Object.assign(new Error('Request failed with status code 400'), {
      config: { data: 'grant_type=refresh_token&refresh_token=refresh-1' },
    })
    refresh.mockRejectedValue(failure)
    const written = (['log', 'info', 'warn', 'error', 'debug'] as const).map((level) => vi.spyOn(console, level).mockImplementation(() => {}))

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(0)

    expect(refresh).toHaveBeenCalledTimes(1)
    const printed = JSON.stringify(written.flatMap((spy) => spy.mock.calls), (_key, value) => (value instanceof Error ? { ...value, message: value.message } : value))
    expect(printed).not.toContain('refresh-1')
    written.forEach((spy) => spy.mockRestore())
  })

  it('signs out and says the session ended when the API refuses the refresh token', async () => {
    signInFor(30)
    refresh.mockRejectedValue(axiosError(400, { error: 'invalid_grant', error_description: 'Your session has ended. Sign in again.' }))

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(0)

    expect(localStorage.getItem('accessToken')).toBeNull()
    expect(localStorage.getItem('refreshToken')).toBeNull()
    expect(push).toHaveBeenCalledWith({ name: 'Login', query: { reason: 'session-ended', redirect: '/reports/42' } })
  })

  it('stays signed in when a refresh fails for another reason, such as the network', async () => {
    signInFor(30)
    refresh.mockRejectedValue(axiosError(503))

    await useRefreshToken().setRefreshTokenTimer()
    await vi.advanceTimersByTimeAsync(0)

    expect(localStorage.getItem('refreshToken')).toBe('refresh-1')
    expect(push).not.toHaveBeenCalled()
  })
})
