import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

import { OIDC_POPUP_WINDOW_NAME } from './oidcPopup'

export const signInWithOidcPopup = async (
  issuer: string,
  clientId: string,
): Promise<string> => {
  const manager = new UserManager({
    authority: issuer,
    client_id: clientId,
    redirect_uri: window.location.origin,
    scope: 'openid email profile',
    automaticSilentRenew: false,
    popupWindowTarget: OIDC_POPUP_WINDOW_NAME,
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  })

  const user = await manager.signinPopup({ popupAbortOnClose: true })
  await manager.removeUser()

  if (!user.id_token) {
    throw new Error('No ID token received from the identity provider')
  }
  return user.id_token
}

// The popup-callback instance only relays window.location back to the opener,
// which owns the pending sign-in state — the placeholder authority and
// client_id are never contacted.
export const completeOidcPopupCallback = (): Promise<void> =>
  new UserManager({
    authority: window.location.origin,
    client_id: 'oidc-popup-callback',
    redirect_uri: window.location.origin,
    automaticSilentRenew: false,
  }).signinPopupCallback()
