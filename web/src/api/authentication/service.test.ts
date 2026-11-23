import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import AuthenticationService from './service'

describe('AuthenticationService token requests', () => {
  let service: AuthenticationService
  let postSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new AuthenticationService({ base: 'http://test', withCredentials: false })
    postSpy = vi.fn().mockResolvedValue({ status: 200, data: {} })
    ;(service as unknown as { instance: unknown }).instance = { post: postSpy }
  })

  const CALLS: [string, (service: AuthenticationService) => Promise<unknown>][] = [
    ['a password sign-in', s => s.login({ username: 'analyst@example.com', password: 'P@ssw0rd' })],
    ['a second factor', s => s.verifyMfa({ mfaToken: 'mfa-token', code: '123456', method: 'totp' })],
    ['a session refresh', s => s.refreshToken('refresh-token')],
    ['a Google sign-in', s => s.verifyGoogleToken('google-credential')],
    ['a Microsoft sign-in', s => s.verifyOidcToken('oidc-credential')],
  ]

  it.each(CALLS)('%s names no client', async (_, call) => {
    await call(service)

    const form = postSpy.mock.calls[0][1] as URLSearchParams
    expect(form.has('client_id')).toBe(false)
    expect(form.has('client_secret')).toBe(false)
  })

  describe('with AWS WAF\'s script on the page', () => {
    afterEach(() => {
      delete window.AwsWafIntegration
    })

    it('a Google sign-in, which can create an account, posts only once the firewall has its token', async () => {
      const order: string[] = []
      window.AwsWafIntegration = { getToken: vi.fn(async () => { await Promise.resolve(); order.push('token'); return 'waf-token' }) }
      postSpy.mockImplementation(async () => { order.push('post'); return { status: 200, data: {} } })

      await service.verifyGoogleToken('google-credential')

      expect(order).toEqual(['token', 'post'])
    })

    it('a Microsoft sign-in, which the firewall does not score, asks for no token', async () => {
      const getToken = vi.fn(async () => 'waf-token')
      window.AwsWafIntegration = { getToken }

      await service.verifyOidcToken('oidc-credential')

      expect(getToken).not.toHaveBeenCalled()
    })
  })
})
