<script lang="ts" setup>
import { inject, onBeforeUnmount, onMounted, type HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { FORM_DESCRIPTION_SHOWN_INJECTION_KEY } from './injectionKeys'
import { useFormField } from './useFormField'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()

const { formDescriptionId } = useFormField()

const descriptionShown = inject(FORM_DESCRIPTION_SHOWN_INJECTION_KEY, undefined)
onMounted(() => {
  if (descriptionShown) descriptionShown.value = true
})
onBeforeUnmount(() => {
  if (descriptionShown) descriptionShown.value = false
})
</script>

<template>
  <p
    :id="formDescriptionId"
    :class="cn('text-sm text-muted-foreground', props.class)"
  >
    <slot />
  </p>
</template>
