<script lang="ts" setup>
import { Slot } from 'reka-ui'
import { computed, inject } from 'vue'
import { FORM_DESCRIPTION_SHOWN_INJECTION_KEY } from './injectionKeys'
import { useFormField } from './useFormField'

const props = defineProps<{ describedBy?: string; invalid?: boolean }>()

const { error, formItemId, formDescriptionId, formMessageId } = useFormField()
const descriptionShown = inject(FORM_DESCRIPTION_SHOWN_INJECTION_KEY, undefined)

const describedBy = computed(
  () =>
    [descriptionShown?.value && formDescriptionId, error.value && formMessageId, props.describedBy].filter(Boolean).join(' ') || undefined,
)
</script>

<template>
  <Slot
    :id="formItemId"
    :aria-describedby="describedBy"
    :aria-invalid="!!error || props.invalid"
  >
    <slot />
  </Slot>
</template>
