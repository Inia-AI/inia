import { isAxiosError } from 'axios'
import API from '@/api'
import { isTokenValid, storeRefreshToken } from '@/lib/utils'
import { signInAgain } from '@/lib/sessionEnded'

const INVALID_GRANT = 'invalid_grant'

/** The API refuses a refresh token whose session has ended: revoked, or past a session limit that was shortened. */
const sessionEnded = (error: unknown) =>
  isAxiosError(error) && (error.response?.data as { error?: string } | undefined)?.error === INVALID_GRANT

let TIMEOUT_ID: ReturnType<typeof setTimeout> | null = null

export const useRefreshToken = () => {
  const { mutateAsync: refreshTokenAsync } =
    API.Authentication.useRefreshToken()

  const setRefreshTokenTimer = async () => {
    // if we register timeout before clear it first
    if (TIMEOUT_ID) {
      clearTimeout(TIMEOUT_ID)
    }

    // if token is invalid, skip it
    if (!isTokenValid()) {
      return
    }

    const expirationRaw = localStorage.getItem('expiration')
    if (!expirationRaw) {
      return
    }

    const expiration = new Date(+expirationRaw)
    const currentTime = new Date()
    const diffInMs = Math.floor(expiration.getTime() - currentTime.getTime())

    // The token that reaches the session's limit comes without a refresh token: the session ends when it expires.
    // A user who signed out before then holds no token, and the sign-in screen keeps its own reason.
    if (!localStorage.getItem('refreshToken')) {
      TIMEOUT_ID = setTimeout(() => {
        if (localStorage.getItem('accessToken')) {
          void signInAgain()
        }
      }, diffInMs)
      return
    }

    // refresh token before 1 minute of expiry
    const expiryTime = diffInMs - 60 * 1000

    TIMEOUT_ID = setTimeout(async () => {
      try {
        if (!isTokenValid()) {
          return
        }

        const refreshToken = localStorage.getItem('refreshToken')
        if (!refreshToken) {
          return
        }
        const response = await refreshTokenAsync(refreshToken)

        localStorage.setItem('accessToken', response?.access_token)
        storeRefreshToken(response?.refresh_token)
        const expiry = response?.expires_in * 1000
        localStorage.setItem('expiration', (Date.now() + expiry).toString())

        setRefreshTokenTimer()
      } catch (error) {
        // The error carries the request, refresh token included, so it is never logged.
        if (TIMEOUT_ID) clearTimeout(TIMEOUT_ID)
        if (sessionEnded(error)) {
          await signInAgain()
        }
      }
    }, expiryTime)
  }

  return { setRefreshTokenTimer }
}
