<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type { OptionType } from '@/types'
import { useToast } from '@/components/ui/toast/use-toast'
import { useReferenceId } from '@/composables/useReferenceId'
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
import { IniaSelect } from '@/components/molecules'

const nameFieldId = useId()
const typeFieldId = useId()
const referenceFieldId = useId()

const props = defineProps<{
  open: boolean
  initialName: string
  counterpartyTypeIds: string[]
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  created: [payload: { id: string; name: string }]
}>()

const { t: $t } = useI18n()
const { toast } = useToast()

const name = ref('')
const selectedTypeId = ref<string | undefined>(undefined)
const isSubmitting = ref(false)

const { referenceId, isManuallyEdited, onManualInput, reset: resetReferenceId } =
  useReferenceId(name)

const counterpartyTypeParams = ref({
  $top: 10000,
  $orderby: 'name asc',
})

const { data: allCounterpartyTypes } =
  API.CounterpartyTypes.useGetCounterpartyTypes(counterpartyTypeParams)

const { mutateAsync: createCounterparty } =
  API.Counterparties.useCreateCounterparty()

const typeOptions = computed<OptionType[]>(() => {
  const allTypes = allCounterpartyTypes.value?.value ?? []
  const mappedIds = props.counterpartyTypeIds

  const filtered =
    mappedIds.length > 0
      ? allTypes.filter(t => mappedIds.includes(t.id))
      : allTypes

  return filtered.map(t => ({ label: t.name, value: t.id }))
})

// Auto-select when only one type available
watch(typeOptions, types => {
  if (types.length === 1 && !selectedTypeId.value) {
    selectedTypeId.value = types[0].value
  }
})

// Reset form when dialog opens
watch(
  () => props.open,
  isOpen => {
    if (isOpen) {
      name.value = props.initialName
      selectedTypeId.value = undefined
      isSubmitting.value = false
      resetReferenceId(props.initialName)
      // Trigger auto-select check
      if (typeOptions.value.length === 1) {
        selectedTypeId.value = typeOptions.value[0].value
      }
    }
  },
)

const nameHasForbiddenCharacters = computed(() =>
  containsForbiddenNameCharacters(name.value),
)

const canSubmit = computed(
  () =>
    name.value.trim().length > 0 &&
    !nameHasForbiddenCharacters.value &&
    referenceId.value.length >= 3 &&
    !!selectedTypeId.value &&
    !isSubmitting.value,
)

async function handleSubmit() {
  if (!canSubmit.value) return

  isSubmitting.value = true

  try {
    await createCounterparty({
      id: referenceId.value,
      name: name.value.trim(),
      nameVariants: name.value.trim(), // default to name for quick-create; editable via full form later
      counterpartyTypeIds: [selectedTypeId.value!],
    })

    toast({
      class: 'text-sm',
      description: $t('shared.components.counterpartySelect.success', {
        name: name.value.trim(),
      }),
      duration: 2000,
    })

    emit('created', { id: referenceId.value, name: name.value.trim() })
    emit('update:open', false)
  } catch (error) {
    console.error('Failed to create counterparty:', error)
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: extractApiErrorMessage(error, $t('shared.components.counterpartySelect.error')),
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
          {{ $t('shared.components.counterpartySelect.dialog.title') }}
        </DialogTitle>
        <DialogDescription class="sr-only">
          {{ $t('shared.components.counterpartySelect.dialog.description') }}
        </DialogDescription>
      </DialogHeader>

      <form class="flex flex-col gap-4" @submit.prevent="handleSubmit">
        <div class="space-y-2">
          <label :for="nameFieldId" class="text-sm font-medium text-[var(--text-primary)]">
            {{ $t('shared.components.counterpartySelect.dialog.nameLabel') }}
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

        <div class="space-y-2">
          <label :for="typeFieldId" class="text-sm font-medium text-[var(--text-primary)]">
            {{ $t('shared.components.counterpartySelect.dialog.typeLabel') }}
          </label>
          <IniaSelect
            :id="typeFieldId"
            v-model="selectedTypeId"
            :options="typeOptions"
            :placeholder="$t('shared.components.counterpartySelect.dialog.typePlaceholder')"
          />
        </div>

        <div class="space-y-1">
          <label :for="referenceFieldId" class="text-sm font-medium text-[var(--text-primary)]">
            {{ $t('shared.components.counterpartySelect.referenceId.label') }}
          </label>
          <Input
            :id="referenceFieldId"
            :model-value="referenceId"
            :class="{ 'text-muted-foreground italic': !isManuallyEdited && referenceId.length > 0 }"
            :placeholder="$t('shared.components.counterpartySelect.referenceId.placeholder')"
            @update:model-value="onManualInput($event as string)"
          />
          <p v-if="referenceId.length > 0 && referenceId.length < 3" class="text-xs text-error">
            {{ $t('shared.components.counterpartySelect.referenceId.validation.minLength') }}
          </p>
          <p v-else class="text-xs text-muted-foreground">
            {{ $t('shared.components.counterpartySelect.referenceId.hint') }}
          </p>
        </div>

        <div class="flex justify-end gap-3 mt-2">
          <Button
            type="button"
            variant="outline"
            @click="emit('update:open', false)"
          >
            {{ $t('shared.components.counterpartySelect.dialog.cancel') }}
          </Button>
          <Button type="submit" :disabled="!canSubmit">
            {{ $t('shared.components.counterpartySelect.dialog.submit') }}
          </Button>
        </div>
      </form>
    </DialogContent>
  </Dialog>
</template>
