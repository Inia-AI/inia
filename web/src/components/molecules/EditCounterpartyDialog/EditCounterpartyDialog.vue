<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type { CounterpartyDetails } from '@/api/counterparties/schema'
import { useToast } from '@/components/ui/toast/use-toast'
import { containsForbiddenNameCharacters, COUNTERPARTY_NAME_MAX_LENGTH } from '@/utils/counterpartyName'

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { IniaLoader } from '@/components/atoms'

const nameFieldId = useId()

const props = defineProps<{
  open: boolean
  counterpartyId: string | null
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  updated: [payload: { id: string; name: string }]
}>()

const { t: $t } = useI18n()
const { toast } = useToast()

const { mutateAsync: getCounterparty } = API.Counterparties.useGetCounterparty()
const { mutateAsync: updateCounterparty } =
  API.Counterparties.updateCounterparty()

const details = ref<CounterpartyDetails | null>(null)
const name = ref('')
const isLoading = ref(false)
const isSubmitting = ref(false)

// Collapse runs of any whitespace (NBSP included) and trim, so visually-identical
// inputs like "Acme  Corp" vs "Acme Corp" are treated as the same.
function normalizeName(raw: string) {
  return raw.replace(/\s+/g, ' ').trim()
}

async function loadDetails(id: string) {
  isLoading.value = true
  try {
    const fetched = (await getCounterparty(id)) as CounterpartyDetails
    details.value = fetched
    name.value = fetched.name
  } catch (error) {
    console.error('Failed to load counterparty:', error)
    toast({
      class: 'text-sm',
      description: $t('shared.components.counterpartySelect.editError'),
      variant: 'destructive',
      duration: 3000,
    })
    emit('update:open', false)
  } finally {
    isLoading.value = false
  }
}

watch(
  [() => props.open, () => props.counterpartyId],
  ([isOpen, id], [, prevId]) => {
    if (!isOpen) {
      details.value = null
      name.value = ''
      return
    }
    if (!id) return
    // Skip the refetch when the id is unchanged AND we already have details
    // (e.g. spurious watcher fire). Otherwise reload for the new target.
    if (id === prevId && details.value?.id === id) return
    loadDetails(id)
  },
  { immediate: true },
)

const nameHasForbiddenCharacters = computed(() =>
  containsForbiddenNameCharacters(name.value),
)

const canSubmit = computed(() => {
  if (!details.value || isSubmitting.value) return false
  if (nameHasForbiddenCharacters.value) return false
  const normalized = normalizeName(name.value)
  if (normalized.length === 0) return false
  if (normalized === normalizeName(details.value.name)) return false
  return true
})

async function handleSubmit() {
  if (!canSubmit.value || !details.value) return
  const normalized = normalizeName(name.value)
  const current = details.value
  isSubmitting.value = true
  try {
    const nextVariants =
      current.nameVariants === current.name ? normalized : current.nameVariants

    await updateCounterparty({
      id: current.id,
      params: {
        parentId: current.parentId,
        counterpartyTypeIds: current.counterpartyTypeIds,
        name: normalized,
        nameVariants: nextVariants,
        email: current.email,
        phoneNumber: current.phoneNumber,
        website: current.website,
        address: current.address,
        lastFinancialAnalysisResult: current.lastFinancialAnalysisResult,
      },
    })

    toast({
      class: 'text-sm',
      description: $t('shared.components.counterpartySelect.editSuccess', {
        name: normalized,
      }),
      duration: 2000,
    })

    emit('updated', { id: current.id, name: normalized })
    emit('update:open', false)
  } catch (error) {
    console.error('Failed to update counterparty:', error)
    toast({
      class: 'text-sm',
      description: extractApiErrorMessage(error, $t('shared.components.counterpartySelect.editError')),
      variant: 'destructive',
      duration: 3000,
    })
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>
          {{ $t('shared.components.counterpartySelect.editDialog.title') }}
        </DialogTitle>
        <DialogDescription class="sr-only">
          {{ $t('shared.components.counterpartySelect.editDialog.description') }}
        </DialogDescription>
      </DialogHeader>

      <div v-if="isLoading" class="flex justify-center py-6">
        <IniaLoader />
      </div>

      <form
        v-else-if="details"
        class="flex flex-col gap-4"
        @submit.prevent="handleSubmit"
      >
        <div class="space-y-2">
          <label :for="nameFieldId" class="text-sm font-medium text-[var(--text-primary)]">
            {{ $t('shared.components.counterpartySelect.editDialog.nameLabel') }}
          </label>
          <Input
            :id="nameFieldId"
            v-model="name"
            :maxlength="COUNTERPARTY_NAME_MAX_LENGTH"
            :placeholder="$t('shared.components.counterpartySelect.dialog.namePlaceholder')"
          />
          <p
            v-if="nameHasForbiddenCharacters"
            class="text-sm font-medium text-error"
          >
            {{ $t('shared.components.counterpartySelect.nameForbiddenCharacters') }}
          </p>
        </div>

        <div class="flex justify-end gap-3 mt-2">
          <Button
            type="button"
            variant="outline"
            @click="emit('update:open', false)"
          >
            {{ $t('shared.components.counterpartySelect.editDialog.cancel') }}
          </Button>
          <Button type="submit" :disabled="!canSubmit">
            {{ $t('shared.components.counterpartySelect.editDialog.submit') }}
          </Button>
        </div>
      </form>
    </DialogContent>
  </Dialog>
</template>
