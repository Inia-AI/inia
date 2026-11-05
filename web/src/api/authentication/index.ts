import {
  useMutation,
  useQuery,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import Service from './service'
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

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useLogin(): UseMutationReturnType<
    { status: number; data: any },
    Error,
    TokenRequest,
    unknown
  > {
    return useMutation({
      mutationFn: (params: TokenRequest) => this.service.login(params),
    })
  }

  useVerifyMfa(): UseMutationReturnType<any, Error, MfaVerifyRequest, unknown> {
    return useMutation({
      mutationFn: (params: MfaVerifyRequest) => this.service.verifyMfa(params),
    })
  }

  useSendEmailOtp(): UseMutationReturnType<any, Error, string, unknown> {
    return useMutation({
      mutationFn: (mfaToken: string) => this.service.sendEmailOtp(mfaToken),
    })
  }

  useRefreshToken(): UseMutationReturnType<any, Error, string, unknown> {
    return useMutation({
      mutationFn: (_refreshToken: string) =>
        this.service.refreshToken(_refreshToken),
    })
  }

  // 2FA Management Hooks
  useTwoFactorStatus(): UseQueryReturnType<TwoFactorStatusResponse, Error> {
    return useQuery({
      queryKey: ['twoFactorStatus'],
      queryFn: () => this.service.getTwoFactorStatus(),
    })
  }

  useSetupTotp(): UseMutationReturnType<TotpSetupResponse, Error, void, unknown> {
    return useMutation({
      mutationFn: () => this.service.setupTotp(),
    })
  }

  useEnableTotp(): UseMutationReturnType<
    EnableTotpResponse,
    Error,
    EnableTotpRequest,
    unknown
  > {
    return useMutation({
      mutationFn: (params: EnableTotpRequest) => this.service.enableTotp(params),
    })
  }

  useDisableTotp(): UseMutationReturnType<void, Error, DisableTotpRequest, unknown> {
    return useMutation({
      mutationFn: (params: DisableTotpRequest) => this.service.disableTotp(params),
    })
  }

  useRegenerateRecoveryCodes(): UseMutationReturnType<
    EnableTotpResponse,
    Error,
    void,
    unknown
  > {
    return useMutation({
      mutationFn: () => this.service.regenerateRecoveryCodes(),
    })
  }

  useTrustedDevices(): UseQueryReturnType<TrustedDeviceResponse[], Error> {
    return useQuery({
      queryKey: ['trustedDevices'],
      queryFn: () => this.service.getTrustedDevices(),
    })
  }

  useRemoveTrustedDevice(): UseMutationReturnType<void, Error, string, unknown> {
    return useMutation({
      mutationFn: (deviceId: string) => this.service.removeTrustedDevice(deviceId),
    })
  }

  useRemoveAllTrustedDevices(): UseMutationReturnType<void, Error, void, unknown> {
    return useMutation({
      mutationFn: () => this.service.removeAllTrustedDevices(),
    })
  }

  // Direct method for external auth providers (no vue-query needed)
  async getAuthProviders(): Promise<AuthProvidersResponse> {
    return this.service.getAuthProviders()
  }

  // Google Sign-In verification
  async verifyGoogleToken(credential: string): Promise<SignInTokens> {
    return this.service.verifyGoogleToken(credential)
  }

  // OIDC Sign-In verification
  async verifyOidcToken(credential: string): Promise<SignInTokens> {
    return this.service.verifyOidcToken(credential)
  }
}
