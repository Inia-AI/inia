<script lang="ts" setup>
import { Slot } from 'reka-ui'
import { computed, inject } from 'vue'
import { FORM_DESCRIPTION_SHOWN_INJECTION_KEY } from './injectionKeys'
import { useFormField } from './useFormField'

const { error, formItemId, formDescriptionId, formMessageId } = useFormField()
const descriptionShown = inject(FORM_DESCRIPTION_SHOWN_INJECTION_KEY, undefined)

const describedBy = computed(
  () =>
    [descriptionShown?.value && formDescriptionId, error.value && formMessageId].filter(Boolean).join(' ') || undefined,
)
</script>

<template>
  <Slot
    :id="formItemId"
    :aria-describedby="describedBy"
    :aria-invalid="!!error"
  >
    <slot />
  </Slot>
</template>
