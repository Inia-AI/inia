<script setup lang="ts">
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
// TODO: Re-import TwoFactorSettings when 2FA is production-ready
import { ProfileDetails, PasswordResetForm } from '@/components/organisms'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/components/ui/toast/use-toast'
import API from '@/api'
import type { ChangePasswordRequest } from '@/api/profile/schema'
import {
  changePasswordErrorMessages,
  changePasswordErrors,
  localizedIdentityErrors,
  type ChangePasswordErrors,
} from '@/lib/changePasswordErrors'
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { clearSignIn } from '@/lib/utils'

const { t: $t } = useI18n()
const { toast } = useToast()
const router = useRouter()

const { mutateAsync: changePassword } = API.Profile.useChangePassword()
const formIncrement = ref(0)
const serverErrors = ref<ChangePasswordErrors>()

const describeIdentityError = localizedIdentityErrors($t)

const onPasswordReset = async (data: ChangePasswordRequest) => {
  serverErrors.value = undefined
  try {
    await changePassword(data)
    formIncrement.value += 1

    toast({
      class: 'text-sm',
      description: $t(`page.profile.passwordUpdated`),
    })
    clearSignIn()
    await router.push({ name: 'Login' })
  } catch (error) {
    serverErrors.value = changePasswordErrors(error, describeIdentityError)
    const messages = changePasswordErrorMessages(serverErrors.value)

    toast({
      variant: 'destructive',
      class: 'text-sm',
      description:
        messages.length > 0 ? messages.join(' ') : $t(`page.profile.errors.passwordUpdateFailed`),
    })
  }
}
</script>

<template>
  <div class="page-view">
    <div class="page-container">
      <h1 class="page-title">{{ $t('page.profile.title') }}</h1>

      <Tabs default-value="details" class="profile-tabs">
        <TabsList class="tabs-list">
          <TabsTrigger value="details" class="tab-trigger">
            {{ $t('page.profile.tabs.details') }}
          </TabsTrigger>
          <TabsTrigger value="password" class="tab-trigger">
            {{ $t('page.profile.tabs.password') }}
          </TabsTrigger>
          <!-- TODO: Re-enable Security tab when 2FA is production-ready
          <TabsTrigger value="security" class="tab-trigger">
            {{ $t('page.profile.tabs.security') }}
          </TabsTrigger>
          -->
        </TabsList>
        <TabsContent value="details" class="tab-content">
          <ProfileDetails />
        </TabsContent>
        <TabsContent value="password" class="tab-content">
          <PasswordResetForm
            :increment="formIncrement"
            :server-errors="serverErrors"
            @submit="onPasswordReset"
          />
        </TabsContent>
        <!-- TODO: Re-enable Security tab when 2FA is production-ready
        <TabsContent value="security" class="tab-content">
          <TwoFactorSettings />
        </TabsContent>
        -->
      </Tabs>
    </div>
  </div>
</template>

<style scoped>
/* ========================================
   PROFILE VIEW - COMPONENT-SPECIFIC STYLES
   Custom tab styling unique to this view
   ======================================== */

/* Tabs container */
.profile-tabs {
  width: 100%;
}

/* Tab list styling */
.tabs-list {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: 4px;
  display: inline-flex;
  gap: 4px;
  margin-bottom: var(--spacing-md);
}

/* Individual tab trigger */
.tab-trigger {
  padding: 10px 20px;
  border-radius: var(--radius-small);
  font-size: 14px;
  font-weight: 600;
  color: var(--text-secondary);
  transition: all 0.15s ease-in-out;
  cursor: pointer;
  border: none;
  background: transparent;
}

.tab-trigger:hover {
  color: var(--text-primary);
  background: rgba(255, 255, 255, 0.05);
}

.tab-trigger[data-state='active'] {
  background: var(--primary);
  color: var(--primary-foreground);
  box-shadow: 0 2px 8px rgba(26, 56, 80, 0.3);
}

/* Tab content with fade animation */
.tab-content {
  animation: fadeIn 0.3s ease-out;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* Responsive: full-width tabs on mobile */
@media (max-width: 768px) {
  .tabs-list {
    width: 100%;
  }

  .tab-trigger {
    flex: 1;
  }
}
</style>
