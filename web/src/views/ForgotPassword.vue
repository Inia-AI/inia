<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import API from '@/api'
import type { ForgotPasswordRequest } from '@/api/password/schema'
import { useToast } from '@/components/ui/toast/use-toast'

import { ForgotPasswordForm } from '@/components/organisms'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { Button } from '@/components/ui/button'

const { mutateAsync: sendForgotPassword } = API.Password.useForgotPassword()

const { toast } = useToast()
const { t: $t } = useI18n()
const router = useRouter()

const isSent = ref(false)
const resetAvailable = ref<boolean | null>(null)

onMounted(async () => {
  try {
    resetAvailable.value = (await API.Authentication.getAuthProviders()).passwordReset === true
  } catch {
    resetAvailable.value = false
  }
})

const handleSubmit = async (data: ForgotPasswordRequest) => {
  try {
    await sendForgotPassword(data)
    isSent.value = true
  } catch {
    toast({
      variant: 'destructive',
      class: 'text-sm',
      description: $t(`page.forgotPassword.errors.requestFailed`),
    })
  }
}

const handleLoginNow = () => {
  router.push({ name: 'Login' })
}
</script>

<template>
  <div class="forgot-password-view">
    <div class="forgot-password-container">
      <div v-if="isSent" class="success-state">
        <IniaIcon name="circle-check" class="success-icon" />

        <h1 class="success-title">
          {{ $t('page.forgotPassword.sentTitle') }}
        </h1>

        <p class="success-description">
          {{ $t('page.forgotPassword.sentDescription') }}
        </p>

        <Button class="btn-gradient-primary" @click="handleLoginNow">
          {{ $t('page.forgotPassword.sentLoginNow') }}
        </Button>
      </div>
      <div v-else-if="resetAvailable === false" class="success-state">
        <h1 class="page-title">{{ $t('page.forgotPassword.title') }}</h1>
        <p class="success-description">{{ $t('page.forgotPassword.unavailable') }}</p>
        <Button class="btn-gradient-primary" @click="handleLoginNow">
          {{ $t('page.forgotPassword.sentLoginNow') }}
        </Button>
      </div>
      <div v-else-if="resetAvailable">
        <h1 class="page-title">{{ $t('page.forgotPassword.title') }}</h1>
        <p class="page-subtitle">
          {{ $t('page.forgotPassword.description') }}
        </p>
        <ForgotPasswordForm @submit="handleSubmit" />
      </div>
      <h1 v-else class="sr-only">{{ $t('page.forgotPassword.title') }}</h1>
    </div>
  </div>
</template>

<style scoped>
/* ========================================
   FORGOT PASSWORD - COMPONENT-SPECIFIC STYLES
   Only unique styles for this view
   ======================================== */

/* Full-height centered layout */
.forgot-password-view {
  width: 100%;
  height: 100vh;
  background: var(--bg-0);
  display: flex;
  justify-content: center;
  align-items: center;
  padding: var(--spacing-md);
}

.forgot-password-container {
  width: 100%;
  max-width: 512px;
}

/* Success state after email sent */
.success-state {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
}

.success-icon {
  font-size: 80px;
  color: var(--accent-success);
  margin-bottom: var(--spacing-lg);
}

.success-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--spacing-md);
}

.success-description {
  font-size: 14px;
  color: var(--text-secondary);
  margin-bottom: var(--spacing-md);
}

/* Form state styles */
.page-subtitle {
  margin-bottom: var(--spacing-md);
}
</style>
