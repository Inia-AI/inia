<script lang="ts" setup>
import { computed, watch } from 'vue'
import { z } from 'zod'
import { useForm, Field as FormField } from 'vee-validate'
import { useI18n } from 'vue-i18n'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormDescription,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { newPasswordSchema, PASSWORD_MIN_LENGTH } from '@/lib/passwordPolicy'
import type { ChangePasswordErrors } from '@/lib/changePasswordErrors'

const $emits = defineEmits(['submit'])

const props = defineProps<{
  increment: number
  serverErrors?: ChangePasswordErrors
}>()

const { t: $t } = useI18n()

const schema = computed(() =>
  z
    .object({
      currentPassword: z.string(),
      newPassword: newPasswordSchema($t('page.profile.resetPassword.rules.minLength', { minLength: PASSWORD_MIN_LENGTH })),
      confirmPassword: z.string(),
    })
    .refine(data => data.newPassword === data.confirmPassword, {
      path: ['confirmPassword'],
      message: $t('page.profile.resetPassword.rules.mismatch'),
    }),
)

const form = useForm({
  validationSchema: schema,
  initialValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
})

const joinOrClear = (messages: string[] | undefined): string | undefined =>
  messages && messages.length > 0 ? messages.join(' ') : undefined

watch(
  () => props.serverErrors,
  serverErrors => {
    form.setErrors({
      currentPassword: joinOrClear(serverErrors?.currentPassword),
      newPassword: joinOrClear(serverErrors?.newPassword),
    })
  },
)

const handleSubmit = form.handleSubmit(data => {
  const { newPassword, currentPassword } = data
  $emits('submit', { newPassword, currentPassword })
})
</script>

<template>
  <div class="password-reset-form">
    <form @submit="handleSubmit" class="form-container" :key="props.increment">
      <FormField v-slot="{ componentField }" name="currentPassword">
        <FormItem>
          <FormLabel>{{
            $t('page.profile.resetPassword.currentPassword')
          }}</FormLabel>
          <FormControl>
            <Input
              type="password"
              placeholder="Current Password"
              v-bind="componentField"
            />
          </FormControl>
          <FormMessage class="error-message" />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="newPassword">
        <FormItem>
          <FormLabel>{{
            $t('page.profile.resetPassword.newPassword')
          }}</FormLabel>
          <FormControl>
            <Input
              type="password"
              placeholder="New Password"
              v-bind="componentField"
            />
          </FormControl>
          <FormDescription class="hint-message">
            {{ $t('page.profile.resetPassword.passwordRules', { minLength: PASSWORD_MIN_LENGTH }) }}
          </FormDescription>
          <FormMessage class="error-message" />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="confirmPassword">
        <FormItem>
          <FormLabel>{{
            $t('page.profile.resetPassword.confirmPassword')
          }}</FormLabel>
          <FormControl>
            <Input
              type="password"
              placeholder="Confirm Password"
              v-bind="componentField"
            />
          </FormControl>
          <FormMessage class="error-message" />
        </FormItem>
      </FormField>

      <div class="form-actions">
        <Button type="submit" variant="soft" class="submit-button">
          {{ $t('page.profile.resetPassword.submitLabel') }}
        </Button>
      </div>
    </form>
  </div>
</template>

<style scoped>
.password-reset-form {
  width: 100%;
}

.form-container {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: var(--spacing-lg);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  max-width: 600px;
}

.error-message {
  color: var(--accent-error);
  font-size: 13px;
}

.hint-message {
  color: var(--text-secondary);
  font-size: 13px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
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

@media (max-width: 768px) {
  .form-container {
    max-width: 100%;
  }

  .form-actions {
    justify-content: stretch;
  }

  .submit-button {
    width: 100%;
  }
}

/* Deep overrides for form components */
:deep(label) {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 14px;
}

:deep(input) {
  background: var(--bg-0);
  border: 1px solid var(--stroke);
  color: var(--text-primary);
}

:deep(input:focus) {
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 2px rgba(26, 56, 80, 0.1);
}
</style>
