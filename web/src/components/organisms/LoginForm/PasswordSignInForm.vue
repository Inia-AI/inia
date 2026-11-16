<script lang="ts" setup>
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'

import { TokenRequest } from '@/api/schema'
import { FormItem, FormLabel, FormControl, FormMessage } from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

defineProps<{ passwordReset: boolean }>()
const $emits = defineEmits<{ submit: [data: TokenRequest] }>()

// The form and its fields mount together: vee-validate keeps the errors of a first check that finds
// no field yet, and shows them for a moment when the field appears.
const { handleSubmit } = useForm<TokenRequest>({
  validationSchema: z.object({
    username: z.string().email(),
    password: z.string().min(3, 'Password required'),
  }),
  initialValues: { username: '', password: '' },
})

const submit = handleSubmit(data => $emits('submit', data))
</script>

<template>
  <form @submit="submit" class="login-form">
    <FormField v-slot="{ componentField }" name="username">
      <FormItem>
        <FormLabel class="form-label">{{ $t('page.login.form.emailLabel') }}</FormLabel>
        <FormControl>
          <Input
            autocomplete
            v-bind="componentField"
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
</template>

<style scoped>
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
</style>
