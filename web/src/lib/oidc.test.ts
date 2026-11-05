import { describe, it, expect, beforeEach, vi } from 'vitest'

const oidcMocks = vi.hoisted(() => {
  const signinPopup = vi.fn()
  const signinPopupCallback = vi.fn()
  const removeUser = vi.fn()
  const constructorSettings: Record<string, unknown>[] = []
  class UserManagerMock {
    signinPopup = signinPopup
    signinPopupCallback = signinPopupCallback
    removeUser = removeUser
    constructor(settings: Record<string, unknown>) {
      constructorSettings.push(settings)
    }
  }
  return {
    signinPopup,
    signinPopupCallback,
    removeUser,
    constructorSettings,
    UserManagerMock,
  }
})

vi.mock('oidc-client-ts', () => ({
  UserManager: oidcMocks.UserManagerMock,
  WebStorageStateStore: class {},
}))

import { signInWithOidcPopup, completeOidcPopupCallback } from './oidc'
import { OIDC_POPUP_WINDOW_NAME } from './oidcPopup'

beforeEach(() => {
  vi.clearAllMocks()
  oidcMocks.constructorSettings.length = 0
})

describe('signInWithOidcPopup', () => {
  it('runs the popup flow against the given issuer and returns the ID token', async () => {
    oidcMocks.signinPopup.mockResolvedValue({ id_token: 'the-id-token' })

    const token = await signInWithOidcPopup(
      'https://idp.example.com/oauth2/default',
      'client-123',
    )

    expect(token).toBe('the-id-token')
    expect(oidcMocks.constructorSettings).toHaveLength(1)
    const settings = oidcMocks.constructorSettings[0]
    expect(settings.authority).toBe('https://idp.example.com/oauth2/default')
    expect(settings.client_id).toBe('client-123')
    expect(settings.redirect_uri).toBe(window.location.origin)
    expect(settings.scope).toBe('openid email profile')
    expect(settings.popupWindowTarget).toBe(OIDC_POPUP_WINDOW_NAME)
    expect(settings.automaticSilentRenew).toBe(false)
    expect(settings.stateStore).toBeTruthy()
    expect(oidcMocks.signinPopup).toHaveBeenCalledWith({
      popupAbortOnClose: true,
    })
  })

  it('clears the stored IdP user once the token is obtained', async () => {
    oidcMocks.signinPopup.mockResolvedValue({ id_token: 'tok' })

    await signInWithOidcPopup('https://idp.example.com', 'client-123')

    expect(oidcMocks.removeUser).toHaveBeenCalledOnce()
  })

  it('throws when the provider response carries no ID token', async () => {
    oidcMocks.signinPopup.mockResolvedValue({ id_token: undefined })

    await expect(
      signInWithOidcPopup('https://idp.example.com', 'client-123'),
    ).rejects.toThrow(/ID token/)
  })
})

describe('completeOidcPopupCallback', () => {
  it('relays the callback URL to the opener window', async () => {
    oidcMocks.signinPopupCallback.mockResolvedValue(undefined)

    await completeOidcPopupCallback()

    expect(oidcMocks.signinPopupCallback).toHaveBeenCalledOnce()
  })
})
