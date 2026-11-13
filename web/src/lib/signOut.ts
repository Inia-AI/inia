import API from '@/api'
import { clearSignIn } from '@/lib/utils'

/**
 * Ends this browser's session on the server, so a copied token stops working too, and forgets it here at once.
 * The browser does not wait for the server: if the call fails, the server ends the session at its own limit.
 */
export function signOut() {
  const refreshToken = localStorage.getItem('refreshToken')
  const accessToken = localStorage.getItem('accessToken')
  clearSignIn()

  const revoked = refreshToken
    ? API.Authentication.service.revokeToken(refreshToken, 'refresh_token')
    : accessToken
      ? API.Authentication.service.revokeToken(accessToken, 'access_token')
      : undefined
  revoked?.catch(() => undefined)
}
