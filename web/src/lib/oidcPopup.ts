export const OIDC_POPUP_WINDOW_NAME = 'inia-oidc-popup'

export const isOidcPopupCallback = (): boolean =>
  window.name === OIDC_POPUP_WINDOW_NAME
