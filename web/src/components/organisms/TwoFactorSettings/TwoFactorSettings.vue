<script setup lang="ts">
import { ref, computed } from 'vue'
import { parseServerDate } from '@/lib/utils'
import { qrCodeImageSource } from '@/lib/qrCode'
import { useQueryClient } from '@tanstack/vue-query'
import API from '@/api'
import { useToast } from '@/components/ui/toast/use-toast'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

const { toast } = useToast()
const { t: $t } = useI18n()
const queryClient = useQueryClient()

// API hooks
const { data: status, isLoading: isLoadingStatus } =
  API.Authentication.useTwoFactorStatus()
const { data: trustedDevices, isLoading: isLoadingDevices } =
  API.Authentication.useTrustedDevices()

const { mutateAsync: setupTotp, isPending: isSettingUp } =
  API.Authentication.useSetupTotp()
const { mutateAsync: enableTotp, isPending: isEnabling } =
  API.Authentication.useEnableTotp()
const { mutateAsync: disableTotp, isPending: isDisabling } =
  API.Authentication.useDisableTotp()
const { mutateAsync: regenerateCodes, isPending: isRegenerating } =
  API.Authentication.useRegenerateRecoveryCodes()
const { mutateAsync: removeTrustedDevice, isPending: isRemovingDevice } =
  API.Authentication.useRemoveTrustedDevice()
const { mutateAsync: removeAllTrustedDevices, isPending: isRemovingAll } =
  API.Authentication.useRemoveAllTrustedDevices()

// State
const showSetupDialog = ref(false)
const showDisableDialog = ref(false)
const showRecoveryCodesDialog = ref(false)
const setupData = ref<{ sharedKey: string; authenticatorUri: string } | null>(null)
const qrImage = ref('')
const recoveryCodes = ref<string[]>([])
const verificationCode = ref('')
const disableCode = ref('')
const codeError = ref('')

const isLoading = computed(
  () =>
    isSettingUp.value ||
    isEnabling.value ||
    isDisabling.value ||
    isRegenerating.value ||
    isRemovingDevice.value ||
    isRemovingAll.value
)

const invalidateQueries = () => {
  queryClient.invalidateQueries({ queryKey: ['twoFactorStatus'] })
  queryClient.invalidateQueries({ queryKey: ['trustedDevices'] })
}

const handleStartSetup = async () => {
  try {
    const data = await setupTotp()
    setupData.value = data
    qrImage.value = ''
    verificationCode.value = ''
    codeError.value = ''
    showSetupDialog.value = true
    qrImage.value = await qrCodeImageSource(data.authenticatorUri).catch(() => '')
  } catch {
    toast({
      variant: 'destructive',
      description: $t('page.twoFactor.settings.error.setup'),
    })
  }
}

const handleEnableTotp = async () => {
  if (!verificationCode.value) {
    codeError.value = $t('page.twoFactor.codeRequired')
    return
  }

  codeError.value = ''

  try {
    const result = await enableTotp({
      code: verificationCode.value.replace(/\s|-/g, ''),
    })
    recoveryCodes.value = result.recoveryCodes
    showSetupDialog.value = false
    showRecoveryCodesDialog.value = true
    invalidateQueries()
    toast({
      description: $t('page.twoFactor.settings.success.enabled'),
    })
  } catch {
    codeError.value = $t('page.twoFactor.invalidCode')
  }
}

const handleOpenDisableDialog = () => {
  disableCode.value = ''
  codeError.value = ''
  showDisableDialog.value = true
}

const handleDisableTotp = async () => {
  if (!disableCode.value) {
    codeError.value = $t('page.twoFactor.codeRequired')
    return
  }

  codeError.value = ''

  try {
    await disableTotp({
      code: disableCode.value.replace(/\s|-/g, ''),
    })
    showDisableDialog.value = false
    invalidateQueries()
    toast({
      description: $t('page.twoFactor.settings.success.disabled'),
    })
  } catch {
    codeError.value = $t('page.twoFactor.invalidCode')
  }
}

const handleRegenerateCodes = async () => {
  try {
    const result = await regenerateCodes()
    recoveryCodes.value = result.recoveryCodes
    showRecoveryCodesDialog.value = true
    invalidateQueries()
    toast({
      description: $t('page.twoFactor.settings.success.regenerated'),
    })
  } catch {
    toast({
      variant: 'destructive',
      description: $t('page.twoFactor.settings.error.regenerate'),
    })
  }
}

const handleRemoveDevice = async (deviceId: string) => {
  try {
    await removeTrustedDevice(deviceId)
    invalidateQueries()
    toast({
      description: $t('page.twoFactor.settings.success.deviceRemoved'),
    })
  } catch {
    toast({
      variant: 'destructive',
      description: $t('page.twoFactor.settings.error.removeDevice'),
    })
  }
}

const handleRemoveAllDevices = async () => {
  try {
    await removeAllTrustedDevices()
    invalidateQueries()
    toast({
      description: $t('page.twoFactor.settings.success.allDevicesRemoved'),
    })
  } catch {
    toast({
      variant: 'destructive',
      description: $t('page.twoFactor.settings.error.removeDevice'),
    })
  }
}

const copyRecoveryCodes = async () => {
  const text = recoveryCodes.value.join('\n')
  try {
    await navigator.clipboard.writeText(text)
    toast({
      description: $t('page.twoFactor.settings.recoveryCodes.copied'),
    })
  } catch {
    toast({
      variant: 'destructive',
      description: $t('page.twoFactor.settings.error.clipboard'),
    })
  }
}
</script>

<template>
  <div class="two-factor-settings">
    <!-- Status Card -->
    <div class="settings-card">
      <div class="card-header">
        <h3 class="card-title">{{ $t('page.twoFactor.settings.title') }}</h3>
        <p class="card-description">
          {{ $t('page.twoFactor.settings.description') }}
        </p>
      </div>
      <div class="card-content">
        <div v-if="isLoadingStatus" class="loading-state">
          <span class="loading-spinner" />
          {{ $t('common.loading') }}
        </div>
        <div v-else class="status-content">
          <div class="status-indicator">
            <span
              :class="[
                'status-badge',
                status?.isEnabled ? 'status-enabled' : 'status-disabled',
              ]"
            >
              {{
                status?.isEnabled
                  ? $t('page.twoFactor.settings.status.enabled')
                  : $t('page.twoFactor.settings.status.disabled')
              }}
            </span>
          </div>

          <div class="action-buttons">
            <Button
              v-if="!status?.isEnabled"
              :disabled="isLoading"
              @click="handleStartSetup"
            >
              {{ $t('page.twoFactor.settings.enable') }}
            </Button>
            <template v-else>
              <Button
                variant="outline"
                :disabled="isLoading"
                @click="handleRegenerateCodes"
              >
                {{ $t('page.twoFactor.settings.recoveryCodes.regenerate') }}
              </Button>
              <Button
                variant="destructive"
                :disabled="isLoading"
                @click="handleOpenDisableDialog"
              >
                {{ $t('page.twoFactor.settings.disable') }}
              </Button>
            </template>
          </div>

          <!-- Recovery Codes Info -->
          <div v-if="status?.isEnabled" class="recovery-info">
            <p class="recovery-count">
              {{
                $t('page.twoFactor.settings.recoveryCodes.remaining',
                  { count: status.recoveryCodesRemaining },
                  status.recoveryCodesRemaining,
                )
              }}
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- Trusted Devices Card -->
    <div v-if="status?.isEnabled" class="settings-card">
      <div class="card-header">
        <div class="devices-header">
          <div>
            <h3 class="card-title">{{
              $t('page.twoFactor.settings.trustedDevices.title')
            }}</h3>
            <p class="card-description">
              {{ $t('page.twoFactor.settings.trustedDevices.description') }}
            </p>
          </div>
          <Button
            v-if="trustedDevices?.length"
            variant="outline"
            size="sm"
            :disabled="isRemovingAll"
            @click="handleRemoveAllDevices"
          >
            {{ $t('page.twoFactor.settings.trustedDevices.removeAll') }}
          </Button>
        </div>
      </div>
      <div class="card-content">
        <div v-if="isLoadingDevices" class="loading-state">
          <span class="loading-spinner" />
          {{ $t('common.loading') }}
        </div>
        <div v-else-if="!trustedDevices?.length" class="empty-state">
          {{ $t('page.twoFactor.settings.trustedDevices.noDevices') }}
        </div>
        <ul v-else class="devices-list">
          <li
            v-for="device in trustedDevices"
            :key="device.id"
            class="device-item"
          >
            <div class="device-info">
              <span class="device-name">
                {{ device.deviceName || $t('page.twoFactor.settings.trustedDevices.unknownDevice') }}
                <span v-if="device.isCurrent" class="current-badge">
                  {{ $t('page.twoFactor.settings.trustedDevices.current') }}
                </span>
              </span>
              <span class="device-date">
                {{ $t('page.twoFactor.settings.trustedDevices.trustedUntil', { date: parseServerDate(device.trustedUntil).toLocaleDateString() }) }}
              </span>
            </div>
            <Button
              variant="ghost"
              size="sm"
              :disabled="isRemovingDevice"
              @click="handleRemoveDevice(device.id)"
            >
              {{ $t('page.twoFactor.settings.trustedDevices.removeDevice') }}
            </Button>
          </li>
        </ul>
      </div>
    </div>

    <!-- Setup Dialog -->
    <Dialog v-model:open="showSetupDialog">
      <DialogContent class="setup-dialog">
        <DialogHeader>
          <DialogTitle>{{ $t('page.twoFactor.settings.setup.title') }}</DialogTitle>
          <DialogDescription>
            {{ $t('page.twoFactor.settings.setup.step1') }}
          </DialogDescription>
        </DialogHeader>

        <div class="setup-content">
          <!-- QR Code Section -->
          <div class="qr-section">
            <p class="step-text">{{ $t('page.twoFactor.settings.setup.step2') }}</p>
            <div v-if="qrImage" class="qr-code">
              <img
                :src="qrImage"
                alt="QR Code"
                width="200"
                height="200"
              />
            </div>
            <div class="secret-key">
              <p class="text-sm font-medium leading-none">{{ $t('page.twoFactor.settings.setup.secretKey') }}</p>
              <code class="key-display">{{ setupData?.sharedKey }}</code>
            </div>
          </div>

          <!-- Verification Section -->
          <div class="verify-section">
            <p class="step-text">{{ $t('page.twoFactor.settings.setup.step3') }}</p>
            <div class="code-input">
              <Label for="verify-code">
                {{ $t('page.twoFactor.settings.setup.verificationCode') }}
              </Label>
              <Input
                id="verify-code"
                v-model="verificationCode"
                type="text"
                inputmode="numeric"
                pattern="[0-9]*"
                maxlength="6"
                autocomplete="one-time-code"
                :placeholder="$t('page.twoFactor.codePlaceholder')"
                :class="{ error: codeError }"
                @keyup.enter="handleEnableTotp"
              />
              <span v-if="codeError" class="error-text">{{ codeError }}</span>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            :disabled="isEnabling"
            @click="showSetupDialog = false"
          >
            {{ $t('common.cancel') }}
          </Button>
          <Button :disabled="isEnabling || !verificationCode" @click="handleEnableTotp">
            <span v-if="isEnabling" class="loading-spinner" />
            {{ $t('page.twoFactor.settings.enable') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Disable Dialog -->
    <Dialog v-model:open="showDisableDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ $t('page.twoFactor.settings.disableDialog.title') }}
          </DialogTitle>
          <DialogDescription>
            {{ $t('page.twoFactor.settings.disableDialog.description') }}
          </DialogDescription>
        </DialogHeader>

        <div class="disable-content">
          <Label for="disable-code">
            {{ $t('page.twoFactor.enterTotpCode') }}
          </Label>
          <Input
            id="disable-code"
            v-model="disableCode"
            type="text"
            inputmode="numeric"
            pattern="[0-9]*"
            maxlength="6"
            autocomplete="one-time-code"
            :placeholder="$t('page.twoFactor.codePlaceholder')"
            :class="{ error: codeError }"
            @keyup.enter="handleDisableTotp"
          />
          <span v-if="codeError" class="error-text">{{ codeError }}</span>
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            :disabled="isDisabling"
            @click="showDisableDialog = false"
          >
            {{ $t('common.cancel') }}
          </Button>
          <Button
            variant="destructive"
            :disabled="isDisabling || !disableCode"
            @click="handleDisableTotp"
          >
            <span v-if="isDisabling" class="loading-spinner" />
            {{ $t('page.twoFactor.settings.disableDialog.confirm') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Recovery Codes Dialog -->
    <Dialog v-model:open="showRecoveryCodesDialog">
      <DialogContent class="recovery-dialog">
        <DialogHeader>
          <DialogTitle>
            {{ $t('page.twoFactor.settings.recoveryCodes.title') }}
          </DialogTitle>
          <DialogDescription>
            {{ $t('page.twoFactor.settings.recoveryCodes.description') }}
          </DialogDescription>
        </DialogHeader>

        <div class="recovery-codes-content">
          <div class="codes-grid">
            <code v-for="code in recoveryCodes" :key="code" class="recovery-code">
              {{ code }}
            </code>
          </div>
          <Button variant="outline" class="copy-button" @click="copyRecoveryCodes">
            {{ $t('page.twoFactor.settings.recoveryCodes.copy') }}
          </Button>
        </div>

        <DialogFooter>
          <Button @click="showRecoveryCodesDialog = false">{{ $t('common.done') }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
.two-factor-settings {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

.settings-card {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  padding: var(--spacing-lg);
}

.card-header {
  margin-bottom: var(--spacing-md);
}

.card-title {
  font-size: 1.125rem;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 var(--spacing-xs) 0;
}

.card-description {
  font-size: 0.875rem;
  color: var(--text-secondary);
  margin: 0;
}

.card-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.status-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.status-indicator {
  display: flex;
  align-items: center;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  padding: 4px 12px;
  border-radius: 9999px;
  font-size: 0.875rem;
  font-weight: 500;
  white-space: nowrap;
}

.status-enabled {
  background: var(--accent-success-light);
  color: var(--accent-success);
}

.status-disabled {
  background: rgba(239, 68, 68, 0.1);
  color: rgb(239, 68, 68);
}

.action-buttons {
  display: flex;
  gap: var(--spacing-sm);
  flex-wrap: wrap;
}

.recovery-info {
  padding-top: var(--spacing-sm);
  border-top: 1px solid var(--stroke);
}

.recovery-count {
  font-size: 0.875rem;
  color: var(--text-secondary);
}

.devices-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--spacing-md);
}

.devices-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.device-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--spacing-sm) 0;
  border-bottom: 1px solid var(--stroke);
}

.device-item:last-child {
  border-bottom: none;
}

.device-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.device-name {
  font-weight: 500;
}

.current-badge {
  margin-left: var(--spacing-xs);
  font-size: 0.75rem;
  padding: 2px 6px;
  background: var(--accent-info-light);
  color: var(--accent-info);
  border-radius: 4px;
}

.device-date {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.empty-state {
  text-align: center;
  color: var(--text-secondary);
  padding: var(--spacing-lg);
}

.loading-state {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  color: var(--text-secondary);
}

.loading-spinner {
  width: 1rem;
  height: 1rem;
  border: 2px solid transparent;
  border-top-color: currentColor;
  border-radius: 50%;
  animation: spin 0.75s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.setup-content,
.disable-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
  padding: var(--spacing-md) 0;
}

.qr-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-md);
}

.step-text {
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.qr-code {
  background: var(--bg-1);
  padding: var(--spacing-md);
  border-radius: var(--radius-card);
}

.secret-key {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-xs);
  width: 100%;
}

.key-display {
  background: var(--bg-2);
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-small);
  font-family: monospace;
  font-size: 0.875rem;
  letter-spacing: 0.1em;
  user-select: all;
}

.verify-section {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.code-input {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.error-text {
  color: var(--accent-error);
  font-size: 0.75rem;
}

:deep(input.error) {
  border-color: var(--destructive);
}

.recovery-codes-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  padding: var(--spacing-md) 0;
}

.codes-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: var(--spacing-sm);
}

.recovery-code {
  background: var(--bg-2);
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-small);
  font-family: monospace;
  font-size: 0.875rem;
  text-align: center;
}

.copy-button {
  align-self: center;
}

.setup-dialog {
  max-width: 480px;
}

.recovery-dialog {
  max-width: 400px;
}

@media (max-width: 768px) {
  .devices-header {
    flex-direction: column;
  }

  .codes-grid {
    grid-template-columns: 1fr;
  }
}
</style>
