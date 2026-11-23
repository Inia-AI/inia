import { BaseService } from '../base'
import type {
  TokenRequest,
  MfaVerifyRequest,
  TwoFactorStatusResponse,
  TotpSetupResponse,
  EnableTotpRequest,
  EnableTotpResponse,
  DisableTotpRequest,
  TrustedDeviceResponse,
  AuthProvidersResponse,
  SignInTokens,
} from './schema'
import type { ServiceParams } from '../schema'
import { getGrantType, getScope } from '@/config/auth'
import { awaitFirewallToken } from '@/lib/firewallToken'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async login(params: TokenRequest) {
    const formData = new URLSearchParams()
    formData.append('username', params.username)
    formData.append('password', params.password)
    formData.append('grant_type', getGrantType())
    formData.append('scope', getScope())

    const response = await this.instance.post(
      this.getApiPath(`connect/token`),
      formData,
      {
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        validateStatus: status => status === 200 || status === 202,
        signInStep: true,
      }
    )

    // Return both status and data so caller can handle 2FA requirement
    return {
      status: response.status,
      data: response.data,
    }
  }

  async verifyMfa(params: MfaVerifyRequest) {
    const formData = new URLSearchParams()
    formData.append('grant_type', 'mfa')
    formData.append('mfa_token', params.mfaToken)
    formData.append('code', params.code)
    formData.append('method', params.method)
    if (params.trustDevice) {
      formData.append('trust_device', 'true')
    }
    if (params.deviceFingerprint) {
      formData.append('device_fingerprint', params.deviceFingerprint)
    }

    const { data } = await this.instance.post(
      this.getApiPath(`connect/token/mfa`),
      formData,
      {
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        signInStep: true,
      }
    )
    return data
  }

  async sendEmailOtp(mfaToken: string) {
    const { data } = await this.instance.post(
      this.getApiPath(`connect/token/mfa/email`),
      { mfaToken },
      { signInStep: true }
    )
    return data
  }

  async refreshToken(_refreshToken: string) {
    const formData = new URLSearchParams()
    formData.append('grant_type', 'refresh_token')
    formData.append('refresh_token', _refreshToken)

    const { data } =
      (await this.instance.post(this.getApiPath(`connect/token`), formData, {
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
      })) || {}
    return data
  }

  async revokeToken(token: string, tokenTypeHint: 'refresh_token' | 'access_token') {
    const formData = new URLSearchParams()
    formData.append('token', token)
    formData.append('token_type_hint', tokenTypeHint)

    await this.instance.post(this.getApiPath(`connect/revoke`), formData, {
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      signInStep: true,
    })
  }

  // 2FA Management APIs (require authentication)
  async getTwoFactorStatus(): Promise<TwoFactorStatusResponse> {
    const { data } = await this.instance.get(
      this.getApiPath(`auth/2fa/status`)
    )
    return data
  }

  async setupTotp(): Promise<TotpSetupResponse> {
    const { data } = await this.instance.post(
      this.getApiPath(`auth/2fa/totp/setup`)
    )
    return data
  }

  async enableTotp(params: EnableTotpRequest): Promise<EnableTotpResponse> {
    const { data } = await this.instance.post(
      this.getApiPath(`auth/2fa/totp/enable`),
      params
    )
    return data
  }

  async disableTotp(params: DisableTotpRequest): Promise<void> {
    await this.instance.post(this.getApiPath(`auth/2fa/totp/disable`), params)
  }

  async regenerateRecoveryCodes(): Promise<EnableTotpResponse> {
    const { data } = await this.instance.post(
      this.getApiPath(`auth/2fa/recovery-codes/regenerate`)
    )
    return data
  }

  async getTrustedDevices(): Promise<TrustedDeviceResponse[]> {
    const { data } = await this.instance.get(
      this.getApiPath(`auth/2fa/trusted-devices`)
    )
    return data
  }

  async removeTrustedDevice(deviceId: string): Promise<void> {
    await this.instance.delete(
      this.getApiPath(`auth/2fa/trusted-devices/${deviceId}`)
    )
  }

  async removeAllTrustedDevices(): Promise<void> {
    await this.instance.delete(this.getApiPath(`auth/2fa/trusted-devices`))
  }

  // External Auth Providers
  async getAuthProviders(): Promise<AuthProvidersResponse> {
    const { data } = await this.instance.get(this.getApiPath(`auth/providers`))
    return data
  }

  async exchangeToken(grantType: string, fields: Record<string, string>): Promise<SignInTokens> {
    const formData = new URLSearchParams({ grant_type: grantType, ...fields, scope: getScope() })

    const { data } = await this.instance.post(
      this.getApiPath(`connect/token`),
      formData,
      {
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        signInStep: true,
      }
    )
    return data
  }

  // Google Sign-In verification
  async verifyGoogleToken(credential: string) {
    await awaitFirewallToken()
    return this.exchangeToken('google', { credential })
  }

  // OIDC Sign-In verification
  async verifyOidcToken(credential: string) {
    return this.exchangeToken('oidc', { credential })
  }
}
