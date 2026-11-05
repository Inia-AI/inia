<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import API from '@/api'
import { useRefreshToken } from '@/composables/refreshToken'
import { useToast } from '@/components/ui/toast/use-toast'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import type { MfaVerifyRequest } from '@/api/authentication/schema'
import { signInFailure } from '@/lib/signInFailure'
import { storeRefreshToken } from '@/lib/utils'

const router = useRouter()
const { toast } = useToast()
const { t: $t } = useI18n()
const { setRefreshTokenTimer } = useRefreshToken()

const { mutateAsync: verifyMfa, isPending: isVerifying } =
  API.Authentication.useVerifyMfa()
const { mutateAsync: sendEmailOtp, isPending: isSendingEmail } =
  API.Authentication.useSendEmailOtp()

const mfaToken = ref('')
const availableMethods = ref<string[]>([])
const maskedEmail = ref('')
const selectedMethod = ref<'totp' | 'email' | 'recovery'>('totp')
const code = ref('')
const trustDevice = ref(false)
const codeError = ref('')
const emailOtpSent = ref(false)

const isLoading = computed(() => isVerifying.value || isSendingEmail.value)

onMounted(() => {
  // Retrieve MFA session data
  const storedToken = sessionStorage.getItem('mfaToken')
  // Guard against the literal string "undefined" which can happen if undefined was stored
  mfaToken.value = storedToken && storedToken !== 'undefined' ? storedToken : ''
  const methodsJson = sessionStorage.getItem('mfaAvailableMethods')
  availableMethods.value = methodsJson ? JSON.parse(methodsJson) : []
  maskedEmail.value = sessionStorage.getItem('mfaEmail') || ''

  // Redirect to login if no MFA token
  if (!mfaToken.value) {
    console.warn('No valid MFA token found, redirecting to login')
    router.push({ name: 'Login' })
    return
  }

  // Set default method based on availability
  if (availableMethods.value.includes('totp')) {
    selectedMethod.value = 'totp'
  } else if (availableMethods.value.includes('email')) {
    selectedMethod.value = 'email'
  } else if (availableMethods.value.includes('recovery')) {
    selectedMethod.value = 'recovery'
  }
})

const selectMethod = (method: 'totp' | 'email' | 'recovery') => {
  selectedMethod.value = method
  code.value = ''
  codeError.value = ''
  emailOtpSent.value = false
}

const clearSecondFactorStep = () => {
  sessionStorage.removeItem('mfaToken')
  sessionStorage.removeItem('mfaAvailableMethods')
  sessionStorage.removeItem('mfaEmail')
}

const restartSignIn = () => {
  clearSecondFactorStep()
  toast({ variant: 'destructive', description: $t('page.twoFactor.expired') })
  router.push({ name: 'Login' })
}

const handleSendEmailOtp = async () => {
  try {
    await sendEmailOtp(mfaToken.value)
    emailOtpSent.value = true
    toast({
      description: $t('page.twoFactor.emailSent'),
    })
  } catch (error) {
    const failure = signInFailure(error)
    if (failure === 'expired') {
      restartSignIn()
      return
    }

    toast({
      variant: 'destructive',
      description:
        failure === 'rateLimited'
          ? $t('page.twoFactor.tooManyAttempts')
          : $t('page.twoFactor.emailSendFailed'),
    })
  }
}

const handleVerify = async () => {
  if (!code.value) {
    codeError.value = $t('page.twoFactor.codeRequired')
    return
  }

  codeError.value = ''

  try {
    const params: MfaVerifyRequest = {
      mfaToken: mfaToken.value,
      // Recovery codes are issued with a dash and compared as issued.
      code: selectedMethod.value === 'recovery' ? code.value.replace(/\s/g, '') : code.value.replace(/\s|-/g, ''),
      method: selectedMethod.value,
      trustDevice: trustDevice.value,
    }

    const response = await verifyMfa(params)

    clearSecondFactorStep()

    // Store tokens
    localStorage.setItem('accessToken', response?.access_token)
    storeRefreshToken(response?.refresh_token)
    const expiry = response?.expires_in * 1000
    localStorage.setItem('expiration', (Date.now() + expiry).toString())

    setRefreshTokenTimer()

    router.push({ name: 'Home' })
  } catch (error) {
    const failure = signInFailure(error)
    if (failure === 'expired') {
      restartSignIn()
      return
    }

    codeError.value =
      failure === 'rateLimited'
        ? $t('page.twoFactor.tooManyAttempts')
        : failure === 'lockedOut'
          ? $t('page.twoFactor.lockedOut')
          : $t('page.twoFactor.invalidCode')
  }
}

const handleCancel = () => {
  clearSecondFactorStep()
  router.push({ name: 'Login' })
}
</script>

<template>
  <div class="two-factor-view">
    <div class="two-factor-container">
      <div class="two-factor-card">
        <div class="card-header">
          <h1 class="card-title">{{ $t('page.twoFactor.title') }}</h1>
          <p class="card-description">{{ $t('page.twoFactor.description') }}</p>
        </div>

        <div class="card-content">
          <!-- Method Selection -->
          <div v-if="availableMethods.length > 1" class="method-selector">
            <p id="two-factor-method" class="text-sm font-medium leading-none">{{ $t('page.twoFactor.selectMethod') }}</p>
            <div class="method-buttons" role="group" aria-labelledby="two-factor-method">
              <Button
                v-if="availableMethods.includes('totp')"
                :variant="selectedMethod === 'totp' ? 'default' : 'outline'"
                :aria-pressed="selectedMethod === 'totp'"
                size="sm"
                @click="selectMethod('totp')"
              >
                {{ $t('page.twoFactor.methods.totp') }}
              </Button>
              <Button
                v-if="availableMethods.includes('email')"
                :variant="selectedMethod === 'email' ? 'default' : 'outline'"
                :aria-pressed="selectedMethod === 'email'"
                size="sm"
                @click="selectMethod('email')"
              >
                {{ $t('page.twoFactor.methods.email') }}
              </Button>
              <Button
                v-if="availableMethods.includes('recovery')"
                :variant="selectedMethod === 'recovery' ? 'default' : 'outline'"
                size="sm"
                @click="selectMethod('recovery')"
              >
                {{ $t('page.twoFactor.methods.recovery') }}
              </Button>
            </div>
          </div>

          <!-- TOTP Input -->
          <div v-if="selectedMethod === 'totp'" class="code-input-section">
            <Label for="totp-code">{{
              $t('page.twoFactor.enterTotpCode')
            }}</Label>
            <Input
              id="totp-code"
              v-model="code"
              type="text"
              inputmode="numeric"
              pattern="[0-9]*"
              maxlength="6"
              autocomplete="one-time-code"
              :placeholder="$t('page.twoFactor.codePlaceholder')"
              :class="{ error: codeError }"
              @keyup.enter="handleVerify"
            />
            <span v-if="codeError" class="error-text">{{ codeError }}</span>
          </div>

          <!-- Email OTP Section -->
          <div v-if="selectedMethod === 'email'" class="code-input-section">
            <div class="email-otp-header">
              <Label for="email-code">{{
                $t('page.twoFactor.enterEmailCode')
              }}</Label>
              <Button
                variant="link"
                size="sm"
                :disabled="isSendingEmail"
                @click="handleSendEmailOtp"
              >
                {{
                  emailOtpSent
                    ? $t('page.twoFactor.resendCode')
                    : $t('page.twoFactor.sendCode')
                }}
              </Button>
            </div>
            <p v-if="maskedEmail" class="email-hint">
              {{ $t('page.twoFactor.emailHint', { email: maskedEmail }) }}
            </p>
            <Input
              id="email-code"
              v-model="code"
              type="text"
              inputmode="numeric"
              pattern="[0-9]*"
              maxlength="6"
              autocomplete="one-time-code"
              :placeholder="$t('page.twoFactor.codePlaceholder')"
              :class="{ error: codeError }"
              :disabled="!emailOtpSent"
              @keyup.enter="handleVerify"
            />
            <span v-if="codeError" class="error-text">{{ codeError }}</span>
          </div>

          <!-- Recovery Code Input -->
          <div v-if="selectedMethod === 'recovery'" class="code-input-section">
            <Label for="recovery-code">{{
              $t('page.twoFactor.enterRecoveryCode')
            }}</Label>
            <Input
              id="recovery-code"
              v-model="code"
              type="text"
              maxlength="16"
              :placeholder="$t('page.twoFactor.recoveryPlaceholder')"
              :class="{ error: codeError }"
              @keyup.enter="handleVerify"
            />
            <span v-if="codeError" class="error-text">{{ codeError }}</span>
            <p class="recovery-hint">{{ $t('page.twoFactor.recoveryHint') }}</p>
          </div>

          <!-- Trust Device -->
          <div class="trust-device">
            <Checkbox id="trust-device" v-model="trustDevice" />
            <Label for="trust-device" class="trust-label">{{
              $t('page.twoFactor.trustDevice')
            }}</Label>
          </div>
        </div>

        <div class="card-footer">
          <Button variant="outline" :disabled="isLoading" @click="handleCancel">
            {{ $t('common.cancel') }}
          </Button>
          <Button :disabled="isLoading || !code" @click="handleVerify">
            <span v-if="isVerifying" class="loading-spinner" />
            {{ $t('page.twoFactor.verify') }}
          </Button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.two-factor-view {
  width: 100%;
  height: 100vh;
  background: var(--bg-0);
  display: flex;
  justify-content: center;
  align-items: center;
  padding: var(--spacing-md);
}

.two-factor-container {
  width: 100%;
  max-width: 440px;
}

.two-factor-card {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  padding: var(--spacing-xl);
}

.card-header {
  text-align: center;
  margin-bottom: var(--spacing-xl);
}

.card-title {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--spacing-sm);
}

.card-description {
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.card-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

.method-selector {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.method-buttons {
  display: flex;
  gap: var(--spacing-sm);
  flex-wrap: wrap;
}

.code-input-section {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.email-otp-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.email-hint {
  font-size: 0.875rem;
  color: var(--text-secondary);
}

.error-text {
  color: var(--accent-error);
  font-size: 0.75rem;
}

.recovery-hint {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.trust-device {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.trust-label {
  font-weight: normal;
  cursor: pointer;
}

.card-footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-xl);
  padding-top: var(--spacing-lg);
  border-top: 1px solid var(--stroke);
}

.loading-spinner {
  width: 1rem;
  height: 1rem;
  border: 2px solid transparent;
  border-top-color: currentColor;
  border-radius: 50%;
  animation: spin 0.75s linear infinite;
  margin-right: var(--spacing-xs);
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

:deep(input.error) {
  border-color: var(--destructive);
}
</style>
