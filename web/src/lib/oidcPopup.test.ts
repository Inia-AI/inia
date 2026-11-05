import { describe, it, expect, afterEach } from 'vitest'

import { OIDC_POPUP_WINDOW_NAME, isOidcPopupCallback } from './oidcPopup'

afterEach(() => {
  window.name = ''
})

describe('isOidcPopupCallback', () => {
  it('returns false in an ordinary window with no name', () => {
    expect(isOidcPopupCallback()).toBe(false)
  })

  it('returns false in a window named by someone else', () => {
    window.name = 'print-preview'

    expect(isOidcPopupCallback()).toBe(false)
  })

  it('returns true inside the OIDC popup window', () => {
    window.name = OIDC_POPUP_WINDOW_NAME

    expect(isOidcPopupCallback()).toBe(true)
  })
})
