export interface TokenRequest {
  username: string
  password: string
}

export interface MfaVerifyRequest {
  mfaToken: string
  code: string
  method: 'totp' | 'email' | 'recovery'
  trustDevice?: boolean
  deviceFingerprint?: string
}

export interface TwoFactorStatusResponse {
  isEnabled: boolean
  hasAuthenticator: boolean
  recoveryCodesRemaining: number
  availableMethods: string[]
}

export interface TotpSetupResponse {
  sharedKey: string
  authenticatorUri: string
}

export interface EnableTotpRequest {
  code: string
}

export interface EnableTotpResponse {
  recoveryCodes: string[]
}

export interface DisableTotpRequest {
  code: string
}

export interface TrustedDeviceResponse {
  id: string
  deviceName: string | null
  createdAt: string
  trustedUntil: string
  isCurrent: boolean
}

export interface TwoFactorRequiredResponse {
  mfaToken: string
  availableMethods: string[]
  email?: string
}

export interface AuthProvidersResponse {
  providers: { name: string; displayName: string; clientId?: string; issuer?: string }[]
  passwordReset?: boolean
}

export interface SignInTokens {
  access_token: string
  token_type: string
  expires_in: number
  refresh_token?: string
}
