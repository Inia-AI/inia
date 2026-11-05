<script setup lang="ts">
import API from '@/api'
import { LoginForm } from '@/components/organisms'
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useRefreshToken } from '@/composables/refreshToken'
import { useToast } from '@/components/ui/toast/use-toast'
import { useI18n } from 'vue-i18n'
import { signInFailure } from '@/lib/signInFailure'
import { storeRefreshToken } from '@/lib/utils'
import { destinationAfterSignIn, type SignInLanding } from '@/lib/signInDestination'
import type { SignInTokens } from '@/api/authentication/schema'

const { mutateAsync: login } = API.Authentication.useLogin()

const router = useRouter()

const { toast } = useToast()
const { t: $t } = useI18n()

const { setRefreshTokenTimer } = useRefreshToken()

const finishingSignIn = ref(false)

const goAfterSignIn = async (landings: SignInLanding[]) => {
  finishingSignIn.value = true
  try {
    await router.push(await destinationAfterSignIn(landings, router.currentRoute.value.query.redirect))
  } finally {
    finishingSignIn.value = false
  }
}

const onSubmit = async (data: { username: string; password: string }, landings: SignInLanding[]) => {
  try {
    const response = await login(data)

    // Check if 2FA is required (202 status)
    if (response.status === 202) {
      const data = response.data
      if (!data) {
        console.error('Missing response data for 2FA login:', response)
        toast({
          variant: 'destructive',
          class: 'text-sm',
          description: $t(`page.login.errors.loginFailed`),
        })
        return
      }

      // Store MFA token and redirect to 2FA verification
      // Handle both camelCase (mfaToken) and PascalCase (MfaToken) property names
      const mfaToken = data.mfaToken || data.MfaToken
      const availableMethods = data.availableMethods || data.AvailableMethods || []
      const maskedEmail = data.maskedEmail || data.MaskedEmail

      if (!mfaToken) {
        console.error('MFA token missing from response:', data)
        toast({
          variant: 'destructive',
          class: 'text-sm',
          description: $t(`page.login.errors.loginFailed`),
        })
        return
      }

      sessionStorage.setItem('mfaToken', mfaToken)
      sessionStorage.setItem('mfaAvailableMethods', JSON.stringify(availableMethods))
      if (maskedEmail) {
        sessionStorage.setItem('mfaEmail', maskedEmail)
      }
      router.push({ name: 'TwoFactorVerify' })
      return
    }

    // Normal login flow - store tokens
    const tokenData = response.data
    if (!tokenData?.access_token) {
      console.error('Missing token data in login response:', response)
      toast({
        variant: 'destructive',
        class: 'text-sm',
        description: $t(`page.login.errors.loginFailed`),
      })
      return
    }

    localStorage.setItem('accessToken', tokenData.access_token)
    storeRefreshToken(tokenData.refresh_token)
    const expiry = (tokenData.expires_in || 3600) * 1000
    localStorage.setItem('expiration', (Date.now() + expiry).toString())

    setRefreshTokenTimer()

    await goAfterSignIn(landings)
  } catch (error) {
    toast({
      variant: 'destructive',
      class: 'text-sm',
      description:
        signInFailure(error) === 'rateLimited'
          ? $t('page.login.errors.tooManyAttempts')
          : $t('page.login.errors.loginFailed'),
    })
  }
}

const handleExternalLogin = async (tokenResponse: SignInTokens, landings: SignInLanding[]) => {
  localStorage.setItem('accessToken', tokenResponse.access_token)
  storeRefreshToken(tokenResponse.refresh_token)
  const expiry = (tokenResponse.expires_in ?? 3600) * 1000
  localStorage.setItem('expiration', (Date.now() + expiry).toString())

  setRefreshTokenTimer()

  await goAfterSignIn(landings)
}
</script>

<template>
  <div class="login-view">
    <div class="login-container">
      <LoginForm
        :busy="finishingSignIn"
        @submit="onSubmit"
        @signed-in="handleExternalLogin"
      />
    </div>
  </div>
</template>

<style scoped>
.login-view {
  width: 100%;
  height: 100vh;
  overflow-y: auto;
  background: var(--bg-0);
  display: flex;
  padding: var(--spacing-md);
}

.login-container {
  width: 100%;
  max-width: 480px;
  margin: auto;
}
</style>
