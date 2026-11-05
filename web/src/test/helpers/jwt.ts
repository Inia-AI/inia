/**
 * Test helpers for seeding a JWT into `localStorage` so role-gated code paths
 * can be exercised under Vitest. The production `hasRole` / `getDecodedToken`
 * utilities only decode — they don't verify — so a syntactically valid JWT
 * with a base64url-encoded payload is enough.
 */

function base64UrlEncode(input: string): string {
  // Handle UTF-8 code points before btoa (which only accepts Latin-1).
  const bytes = new TextEncoder().encode(input)
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

export function makeJwt(payload: Record<string, unknown>): string {
  const header = base64UrlEncode(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const body = base64UrlEncode(JSON.stringify(payload))
  return `${header}.${body}.signature`
}

export function setAuthToken({
  role,
  exp = 9999999999,
  ...rest
}: { role: string | string[]; exp?: number } & Record<string, unknown>): void {
  localStorage.setItem('accessToken', makeJwt({ role, exp, ...rest }))
}

export function clearAuthToken(): void {
  localStorage.removeItem('accessToken')
}
