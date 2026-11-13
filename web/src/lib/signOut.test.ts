import { beforeEach, describe, expect, it, vi } from 'vitest'

const { revokeToken } = vi.hoisted(() => ({ revokeToken: vi.fn() }))

vi.mock('@/api', () => ({ default: { Authentication: { service: { revokeToken } } } }))

import { signOut } from './signOut'

describe('signOut', () => {
  beforeEach(() => {
    revokeToken.mockReset()
    localStorage.clear()
  })

  it('ends the session on the server with its refresh token, and forgets it here', async () => {
    revokeToken.mockResolvedValue(undefined)
    localStorage.setItem('accessToken', 'access')
    localStorage.setItem('refreshToken', 'refresh')
    localStorage.setItem('expiration', '1')

    signOut()

    expect(revokeToken).toHaveBeenCalledWith('refresh', 'refresh_token')
    expect(['accessToken', 'refreshToken', 'expiration'].map((key) => localStorage.getItem(key))).toEqual([null, null, null])
  })

  it('ends the session with the access token when the session holds no refresh token', async () => {
    revokeToken.mockResolvedValue(undefined)
    localStorage.setItem('accessToken', 'access')

    signOut()

    expect(revokeToken).toHaveBeenCalledWith('access', 'access_token')
  })

  it('signs out here at once, even when the server does not answer', async () => {
    revokeToken.mockRejectedValue(new Error('Network Error'))
    localStorage.setItem('accessToken', 'access')
    localStorage.setItem('refreshToken', 'refresh')

    signOut()
    await Promise.resolve()

    expect(localStorage.getItem('refreshToken')).toBeNull()
  })

  it('sends nothing when the browser holds no session', async () => {
    signOut()

    expect(revokeToken).not.toHaveBeenCalled()
  })
})
