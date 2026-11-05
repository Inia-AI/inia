import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { InternalAxiosRequestConfig } from 'axios'
import { axiosError } from '@/test/helpers/axiosError'

const { push, currentRoute } = vi.hoisted(() => ({
  push: vi.fn(),
  currentRoute: { value: { fullPath: '/reports/42', meta: {} as { isPublic?: boolean } } },
}))
vi.mock('@/plugins/router', () => ({ default: { push, currentRoute } }))

const SESSION_ENDED = { name: 'Login', query: { reason: 'session-ended', redirect: '/reports/42' } }

import { BaseService } from './index'
import AuthenticationService from '../authentication/service'

const answering401 = async (config: InternalAxiosRequestConfig) => {
  throw axiosError(401, { error: 'invalid_code' }, config)
}

const authentication = () => {
  const service = new AuthenticationService({ base: 'https://api.example.com', withCredentials: false })
  service.instance.defaults.adapter = answering401
  return service
}

describe('BaseService', () => {
  beforeEach(() => {
    push.mockReset()
    localStorage.setItem('accessToken', 'signed-in')
  })

  it('sends a signed-in user whose session the API refuses back to the login screen, saying the session ended', async () => {
    const service = new BaseService({ base: 'https://api.example.com', withCredentials: false })
    service.instance.defaults.adapter = answering401

    await service.instance.post('api/v1/workflow-reports').catch(() => undefined)

    expect(push).toHaveBeenCalledWith(SESSION_ENDED)
    expect(localStorage.getItem('accessToken')).toBeNull()
  })

  it('sends a user whose refresh token the API refuses back to the login screen, saying the session ended', async () => {
    await authentication().refreshToken('expired').catch(() => undefined)

    expect(push).toHaveBeenCalledWith(SESSION_ENDED)
  })

  it('keeps the reason when several requests of the ended session are refused at once', async () => {
    const service = new BaseService({ base: 'https://api.example.com', withCredentials: false })
    service.instance.defaults.adapter = answering401

    await Promise.all([1, 2, 3].map(() => service.instance.get('api/v1/workflow-reports').catch(() => undefined)))

    expect(push).toHaveBeenCalledTimes(1)
    expect(push).toHaveBeenCalledWith(SESSION_ENDED)
  })

  it('says nothing about a session to a browser that held no token', async () => {
    vi.useFakeTimers()
    vi.advanceTimersByTime(60_000)
    localStorage.removeItem('accessToken')
    const service = new BaseService({ base: 'https://api.example.com', withCredentials: false })
    service.instance.defaults.adapter = answering401

    await service.instance.get('api/v1/workflow-reports').catch(() => undefined)

    expect(push).toHaveBeenCalledWith({ name: 'Login' })
    vi.useRealTimers()
  })

  it.each([
    ['password', (service: AuthenticationService) => service.login({ username: 'analyst@example.com', password: 'wrong' })],
    ['second factor', (service: AuthenticationService) => service.verifyMfa({ mfaToken: 'token', code: '000000', method: 'totp' })],
    ['email code', (service: AuthenticationService) => service.sendEmailOtp('token')],
  ])('leaves a refused %s step on its own screen, which shows the reason', async (_, step) => {
    await step(authentication()).catch(() => undefined)

    expect(push).not.toHaveBeenCalled()
  })
})
