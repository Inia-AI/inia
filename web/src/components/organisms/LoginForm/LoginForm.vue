<script lang="ts" setup>
import { computed, ref, onMounted, nextTick, watch } from 'vue'
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'

import { TokenRequest } from '@/api/schema'
import API from '@/api'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import IniaWordmark from '@/components/atoms/IniaWordmark/IniaWordmark.vue'
import useThemeStore from '@/store/theme'
import { extensionPoints } from '@/modules/extensionPoints'
import type { SignInTokens } from '@/api/authentication/schema'
import type { SignInLanding } from '@/lib/signInDestination'
import { signInRefusalText } from '@/lib/signInFailure'
import { SESSION_ENDED_REASON } from '@/lib/sessionEnded'

const { t: $t } = useI18n()
const props = defineProps<{ busy?: boolean }>()
const $emits = defineEmits<{
  submit: [data: TokenRequest, landings: SignInLanding[]]
  'signed-in': [tokens: SignInTokens, landings: SignInLanding[]]
}>()
const themeStore = useThemeStore()
const route = useRoute()
const sessionEnded = computed(() => route.query.reason === SESSION_ENDED_REASON)

// Auth providers
interface AuthProvider {
  name: string
  displayName: string
  clientId?: string
  issuer?: string
}
const providers = ref<AuthProvider[]>([])
const passwordReset = ref(false)
const loadingProviders = ref(true)
const providersFailed = ref(false)
const googleLoading = ref(false)
const googleError = ref<string | null>(null)
const oidcLoading = ref(false)
const oidcError = ref<string | null>(null)

// Computed helpers for provider availability
const hasPasswordProvider = computed(() =>
  providers.value.some(p => p.name === 'password')
)
const hasGoogleProvider = computed(() =>
  providers.value.some(p => p.name === 'google')
)
const googleProvider = computed(() =>
  providers.value.find(p => p.name === 'google')
)
const hasOidcProvider = computed(() =>
  providers.value.some(p => p.name === 'oidc')
)
const oidcProvider = computed(() =>
  providers.value.find(p => p.name === 'oidc')
)
const signInMethods = computed(() =>
  extensionPoints.signInMethods.filter(method => providers.value.some(p => p.name === method.provider))
)
const hasExternalProviders = computed(() =>
  providers.value.some(p => p.name === 'google' || p.name === 'oidc')
)
const leadingMethod = computed(() => signInMethods.value[0])
const methodNotices = computed(() => signInMethods.value.flatMap(method => (method.notice ? [method.notice] : [])))
const landings = () => signInMethods.value.flatMap(method => (method.landing ? [method.landing] : []))

const googleRefusalKeys = computed(() =>
  Object.assign({}, ...signInMethods.value.map(method => method.googleRefusalKeys ?? {})) as Record<string, string>
)

const scriptLoadCache = new Map<string, Promise<void>>()
const loadScript = (src: string, checkLoaded: () => boolean): Promise<void> => {
  if (checkLoaded()) {
    return Promise.resolve()
  }

  const existing = scriptLoadCache.get(src)
  if (existing) {
    return existing
  }

  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = src
    script.async = true
    script.defer = true
    script.onload = () => resolve()
    script.onerror = () => {
      scriptLoadCache.delete(src)
      reject(new Error(`Failed to load script: ${src}`))
    }
    document.head.appendChild(script)
  })

  scriptLoadCache.set(src, promise)
  return promise
}

// Initialize Google Sign-In
const initializeGoogleSignIn = async (clientId: string) => {
  try {
    await loadScript('https://accounts.google.com/gsi/client', () => !!window.google?.accounts)

    if (!window.google?.accounts?.id) {
      console.error('Google Sign-In SDK not loaded properly')
      return
    }

    window.google.accounts.id.initialize({
      client_id: clientId,
      callback: handleGoogleCallback,
      auto_select: false,
      cancel_on_tap_outside: true,
    })

    await nextTick()
    renderGoogleButton()
  } catch (error) {
    console.error('Failed to initialize Google Sign-In:', error)
  }
}

const GOOGLE_BUTTON_THEME = { light: 'outline', dark: 'filled_black' } as const

const renderGoogleButton = () => {
  const buttonContainer = document.getElementById('google-signin-button')
  if (!buttonContainer || !window.google?.accounts?.id) {
    return
  }
  buttonContainer.replaceChildren()
  window.google.accounts.id.renderButton(buttonContainer, {
    theme: GOOGLE_BUTTON_THEME[themeStore.theme],
    size: 'large',
    width: buttonContainer.offsetWidth || 300,
    text: 'continue_with',
    shape: 'rectangular',
  })
}

watch(() => themeStore.theme, renderGoogleButton)

// Handle Google callback
const handleGoogleCallback = async (response: { credential: string }) => {
  googleLoading.value = true
  googleError.value = null

  try {
    // Exchange Google credential for our token
    const result = await API.Authentication.verifyGoogleToken(response.credential)

    $emits('signed-in', result, landings())
  } catch (error: unknown) {
    console.error('Google login failed:', error)
    googleError.value = signInRefusalText(error, $t, googleRefusalKeys.value, 'page.login.errors.googleFailed')
  } finally {
    googleLoading.value = false
  }
}

// Handle OIDC Sign-In button click (any OIDC-compliant provider)
const handleOidcSignIn = async () => {
  const provider = oidcProvider.value
  if (!provider?.clientId || !provider?.issuer) {
    oidcError.value = $t('page.login.form.ssoMisconfigured')
    return
  }

  oidcLoading.value = true
  oidcError.value = null

  try {
    const { signInWithOidcPopup } = await import('@/lib/oidc')
    const idToken = await signInWithOidcPopup(provider.issuer, provider.clientId)

    // Exchange ID token for our token
    const result = await API.Authentication.verifyOidcToken(idToken)

    $emits('signed-in', result, landings())
  } catch (error: unknown) {
    console.error('SSO login failed:', error)
    // Never the error's own message: a chunk-load failure names internal URLs
    oidcError.value = signInRefusalText(error, $t, {}, 'page.login.form.ssoFailed')
  } finally {
    oidcLoading.value = false
  }
}

onMounted(async () => {
  try {
    const response = await API.Authentication.getAuthProviders()
    providers.value = response.providers || []
    passwordReset.value = response.passwordReset === true

  } catch {
    providersFailed.value = true
  } finally {
    loadingProviders.value = false
  }

  if (googleProvider.value?.clientId) {
    await initializeGoogleSignIn(googleProvider.value.clientId)
  }
})

const formData = ref<TokenRequest>({
  username: '',
  password: '',
})

const schema = computed(() =>
  z.object({
    username: z.string().email(),
    password: z.string().min(3, 'Password required'),
  }),
)

const form = useForm<TokenRequest>({
  validationSchema: schema,
  initialValues: formData.value,
})

const handleSubmit = form.handleSubmit(async data => {
  $emits('submit', data, landings())
})
</script>

<template>
  <div class="login-form-container">
    <div class="login-header">
      <IniaWordmark label="Inia AI logo" class="login-logo" />
      <h1 class="login-title" :class="{ invisible: loadingProviders }">{{ $t(leadingMethod?.titleKey ?? 'page.login.form.title') }}</h1>
      <p class="login-subtitle" :class="{ invisible: loadingProviders }">{{ $t(leadingMethod?.subtitleKey ?? 'page.login.form.subtitle') }}</p>
    </div>

    <p v-if="sessionEnded" class="session-ended" role="status" data-testid="session-ended">
      {{ $t('page.login.errors.sessionEnded') }}
    </p>

    <p v-if="providersFailed" class="providers-error" role="alert" data-testid="sign-in-unavailable">
      {{ $t('page.login.errors.providersFailed') }}
    </p>

    <component
      :is="method.form"
      v-for="method in signInMethods"
      :key="method.provider"
      :busy="props.busy"
      @signed-in="(tokens: SignInTokens) => $emits('signed-in', tokens, landings())"
    />
    <div v-if="signInMethods.length > 0 && (hasPasswordProvider || hasExternalProviders)" class="divider method-divider" data-testid="sign-in-divider">
      <span class="divider-text">{{ $t('page.login.form.or') }}</span>
    </div>

    <!-- Password Login Form -->
    <form v-if="hasPasswordProvider" @submit="handleSubmit" class="login-form">
      <FormField v-slot="{ componentField }" name="username">
        <FormItem>
          <FormLabel class="form-label">{{ $t('page.login.form.emailLabel') }}</FormLabel>
          <FormControl>
            <Input
              autocomplete
              v-bind="componentField"
              v-model="formData.username"
              :placeholder="$t('page.login.form.emailPlaceholder')"
              class="form-input"
            />
          </FormControl>
          <FormMessage class="form-error" />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="password">
        <FormItem>
          <FormLabel class="form-label">{{ $t('page.login.form.passwordLabel') }}</FormLabel>
          <FormControl>
            <Input
              autocomplete
              v-bind="componentField"
              v-model="formData.password"
              :placeholder="$t('page.login.form.passwordPlaceholder')"
              type="password"
              class="form-input"
            />
          </FormControl>
          <FormMessage class="form-error" />
        </FormItem>
      </FormField>

      <div class="form-actions">
        <Button type="submit" variant="soft" class="submit-button">
          {{ $t('page.login.form.submitLabel') }}
        </Button>

        <RouterLink
          v-if="passwordReset"
          :to="{ name: 'ForgotPassword' }"
          class="forgot-password-link"
          >{{ $t('page.login.form.forgotPassword') }}</RouterLink
        >
      </div>
    </form>

    <!-- External Auth Providers -->
    <div
      v-if="!loadingProviders && hasExternalProviders"
      class="external-auth-section"
      :class="{ 'external-auth-section--after-method': signInMethods.length > 0 }"
    >
      <div v-if="hasPasswordProvider && signInMethods.length === 0" class="divider">
        <span class="divider-text">{{ $t('page.login.form.orContinueWith') }}</span>
      </div>

      <!-- Google Sign-In Button (rendered by Google SDK) -->
      <div v-if="hasGoogleProvider" class="google-signin-container">
        <div v-if="googleLoading || props.busy" class="google-loading">
          <span class="loading-spinner"></span>
          <span>{{ $t('page.login.form.signingIn') }}</span>
        </div>
        <div v-show="!googleLoading && !props.busy" id="google-signin-button" class="google-button-wrapper"></div>
        <p v-if="googleError" class="google-error" role="alert" data-testid="google-error">{{ googleError }}</p>
      </div>

      <!-- OIDC Sign-In Button -->
      <div v-if="hasOidcProvider" class="oidc-signin-container">
        <div v-if="oidcLoading" class="oidc-loading">
          <span class="loading-spinner"></span>
          <span>{{ $t('page.login.form.signingIn') }}</span>
        </div>
        <Button
          v-else
          type="button"
          variant="outline"
          class="oidc-signin-button"
          :disabled="oidcLoading"
          @click="handleOidcSignIn"
        >
          {{ $t('page.login.form.signInWith', { provider: oidcProvider?.displayName || $t('page.login.form.ssoDefaultProvider') }) }}
        </Button>
        <p v-if="oidcError" class="oidc-error" role="alert">{{ oidcError }}</p>
      </div>
    </div>

    <component :is="notice" v-for="(notice, index) in methodNotices" :key="index" />
  </div>
</template>

<style scoped>
.login-form-container {
  width: 100%;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: var(--spacing-xl);
  box-shadow: var(--shadow-panel);
}

.login-header {
  margin-bottom: var(--spacing-lg);
  text-align: center;
}

.login-logo {
  width: 54px;
  height: auto;
  margin: 0 auto var(--spacing-md) auto;
  display: block;
  color: var(--text-primary);
}

.login-title {
  font-size: 28px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--spacing-xs);
  letter-spacing: -0.02em;
}

.login-subtitle {
  font-size: 14px;
  color: var(--text-secondary);
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.form-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--spacing-xs);
}

.form-input {
  background: rgba(26, 56, 80, 0.15);
  border: 1px solid var(--accent-indigo-from);
  color: var(--text-primary);
  border-radius: var(--radius-button);
  font-size: 14px;
  padding: 10px 14px;
  transition: all 0.15s ease-in-out;
}

.form-input:focus,
.form-input:focus-visible {
  background: rgba(26, 56, 80, 0.2);
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 3px rgba(26, 56, 80, 0.15);
  outline: none;
}

.form-error {
  color: var(--accent-error);
  font-size: 13px;
  margin-top: var(--spacing-xs);
}

/* Deep override for input component */
:deep(input) {
  background: rgba(26, 56, 80, 0.15) !important;
  border: 1px solid var(--accent-indigo-from) !important;
}

:deep(input:focus),
:deep(input:focus-visible) {
  background: rgba(26, 56, 80, 0.2) !important;
  outline: none !important;
  border-color: var(--accent-indigo-from) !important;
  box-shadow: 0 0 0 3px rgba(26, 56, 80, 0.15) !important;
}

.form-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: var(--spacing-sm);
}

.submit-button {
  background: var(--primary);
  color: var(--primary-foreground);
  border: none;
  border-radius: var(--radius-button);
  padding: 10px 24px;
  font-size: 14px;
  font-weight: 600;
  transition: all 0.15s ease-in-out;
}

.submit-button:hover {
  transform: scale(1.02);
  box-shadow: 0 4px 12px rgba(26, 56, 80, 0.3);
}

.forgot-password-link {
  font-size: 13px;
  color: var(--accent-indigo-from);
  text-decoration: underline;
  cursor: pointer;
  transition: color 0.15s ease-in-out;
}

.forgot-password-link:hover {
  color: var(--accent-indigo-to);
}

/* External Auth Section */
.external-auth-section {
  margin-top: var(--spacing-lg);
}

.external-auth-section--after-method {
  margin-top: 0;
}

.method-divider {
  margin-top: var(--spacing-lg);
}

.divider {
  display: flex;
  align-items: center;
  margin-bottom: var(--spacing-md);
}

.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--stroke);
}

.divider-text {
  padding: 0 var(--spacing-md);
  font-size: 13px;
  color: var(--text-secondary);
  text-transform: lowercase;
}

.google-signin-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
}

.google-button-wrapper {
  width: 100%;
  display: flex;
  justify-content: center;
}

.google-loading {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: 10px 16px;
  color: var(--text-secondary);
  font-size: 14px;
}

.loading-spinner {
  width: 16px;
  height: 16px;
  border: 2px solid var(--stroke);
  border-top-color: var(--accent-indigo-from);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.session-ended {
  color: var(--text-primary);
  font-size: 14px;
  text-align: center;
  margin: 0 0 var(--spacing-md);
}

.providers-error {
  color: var(--accent-error);
  font-size: 14px;
  text-align: center;
  margin: 0;
}

.google-error {
  color: var(--accent-error);
  font-size: 13px;
  margin: 0;
  text-align: center;
}

/* OIDC Sign-In */
.oidc-signin-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
}

.oidc-signin-button {
  width: 100%;
  padding: 10px 16px;
  font-size: 14px;
  font-weight: 600;
  border-radius: var(--radius-button);
}

.oidc-loading {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: 10px 16px;
  color: var(--text-secondary);
  font-size: 14px;
}

.oidc-error {
  color: var(--accent-error);
  font-size: 13px;
  margin: 0;
  text-align: center;
}
</style>
