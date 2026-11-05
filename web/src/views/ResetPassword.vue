<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { isAxiosError } from 'axios'

import API from '@/api'
import type { ProblemDetails } from '@/api/base/errors'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  changePasswordErrorMessages,
  changePasswordErrors,
  localizedIdentityErrors,
  PASSWORD_RULE_KEYS,
} from '@/lib/changePasswordErrors'
import { newPasswordSchema, PASSWORD_MIN_LENGTH } from '@/lib/passwordPolicy'
import { signInFailure } from '@/lib/signInFailure'

const INVALID_LINK = 'PasswordReset.InvalidLink'

const { t: $t } = useI18n()
const route = useRoute()
const { mutateAsync: resetPassword, isPending } = API.Password.useResetPassword()

const email = computed(() => (typeof route.query.email === 'string' ? route.query.email : ''))
const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : ''))
const hasLink = computed(() => email.value !== '' && token.value !== '')

const newPassword = ref('')
const repeatPassword = ref('')
const errors = ref<string[]>([])
const linkSpent = ref(false)
const done = ref(false)

const passwordRules = newPasswordSchema({
  minLength: $t(`${PASSWORD_RULE_KEYS}.minLength`, { minLength: PASSWORD_MIN_LENGTH }),
  uppercase: $t(`${PASSWORD_RULE_KEYS}.uppercase`),
  lowercase: $t(`${PASSWORD_RULE_KEYS}.lowercase`),
  digit: $t(`${PASSWORD_RULE_KEYS}.digit`),
})
const describeIdentityError = localizedIdentityErrors($t)

const handleSubmit = async () => {
  errors.value = []
  linkSpent.value = false

  const checked = passwordRules.safeParse(newPassword.value)
  if (!checked.success) {
    errors.value = checked.error.issues.map(issue => issue.message)
    return
  }

  if (newPassword.value !== repeatPassword.value) {
    errors.value = [$t(`${PASSWORD_RULE_KEYS}.mismatch`)]
    return
  }

  try {
    await resetPassword({ email: email.value, token: token.value, newPassword: newPassword.value })
    done.value = true
  } catch (error) {
    const problems = isAxiosError(error) ? ((error.response?.data as ProblemDetails | undefined)?.errors ?? {}) : {}

    if (INVALID_LINK in problems) {
      linkSpent.value = true
      errors.value = [$t('page.resetPassword.errors.invalidLink')]
    } else if (signInFailure(error) === 'rateLimited') {
      errors.value = [$t('page.resetPassword.errors.tooManyAttempts')]
    } else {
      const rules = changePasswordErrorMessages(changePasswordErrors(error, describeIdentityError))
      errors.value = rules.length > 0 ? rules : [$t('page.resetPassword.errors.requestFailed')]
    }
  }
}
</script>

<template>
  <div class="reset-password-view">
    <div class="reset-password-container">
      <div v-if="done" class="done-state">
        <IniaIcon name="circle-check" class="done-icon" />
        <h1 class="done-title">{{ $t('page.resetPassword.doneTitle') }}</h1>
        <p class="done-description">{{ $t('page.resetPassword.doneDescription') }}</p>
        <Button as-child class="btn-gradient-primary">
          <RouterLink :to="{ name: 'Login' }">{{ $t('page.resetPassword.signIn') }}</RouterLink>
        </Button>
      </div>

      <div v-else-if="!hasLink" class="done-state">
        <h1 class="sr-only">{{ $t('page.resetPassword.title') }}</h1>
        <p class="done-description">{{ $t('page.resetPassword.errors.missingLink') }}</p>
        <RouterLink :to="{ name: 'ForgotPassword' }" class="text-link">{{ $t('page.resetPassword.askAgain') }}</RouterLink>
      </div>

      <div v-else>
        <h1 class="page-title">{{ $t('page.resetPassword.title') }}</h1>
        <p class="page-subtitle">{{ $t('page.resetPassword.description', { email }) }}</p>

        <form class="reset-form" @submit.prevent="handleSubmit">
          <div class="field">
            <Label for="new-password">{{ $t('page.resetPassword.form.newPasswordLabel') }}</Label>
            <Input id="new-password" v-model="newPassword" type="password" autocomplete="new-password" required />
            <p class="hint">{{ $t('page.profile.resetPassword.passwordRules', { minLength: PASSWORD_MIN_LENGTH }) }}</p>
          </div>
          <div class="field">
            <Label for="repeat-password">{{ $t('page.resetPassword.form.confirmPasswordLabel') }}</Label>
            <Input id="repeat-password" v-model="repeatPassword" type="password" autocomplete="new-password" required />
          </div>

          <ul v-if="errors.length > 0" class="error-list" role="alert">
            <li v-for="message in errors" :key="message">{{ message }}</li>
          </ul>
          <RouterLink v-if="linkSpent" :to="{ name: 'ForgotPassword' }" class="text-link">{{ $t('page.resetPassword.askAgain') }}</RouterLink>

          <Button type="submit" variant="soft" :disabled="isPending">
            {{ $t('page.resetPassword.form.submitLabel') }}
          </Button>
        </form>
      </div>
    </div>
  </div>
</template>

<style scoped>
.reset-password-view {
  width: 100%;
  height: 100vh;
  background: var(--bg-0);
  display: flex;
  justify-content: center;
  align-items: center;
  padding: var(--spacing-md);
}

.reset-password-container {
  width: 100%;
  max-width: 512px;
}

.page-subtitle {
  margin-bottom: var(--spacing-md);
}

.reset-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.hint {
  font-size: 13px;
  color: var(--text-secondary);
}

.error-list {
  font-size: 13px;
  color: var(--accent-error);
}

.text-link {
  font-size: 13px;
  color: var(--accent-indigo-from);
  text-decoration: underline;
}

.text-link:hover {
  color: var(--accent-indigo-to);
}

.done-state {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  gap: var(--spacing-md);
}

.done-icon {
  font-size: 80px;
  color: var(--accent-success);
}

.done-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
}

.done-description {
  font-size: 14px;
  color: var(--text-secondary);
}
</style>
