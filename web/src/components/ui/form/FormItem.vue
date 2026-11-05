<script lang="ts" setup>
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { useId } from 'reka-ui'
import { provide, ref } from 'vue'
import { FORM_DESCRIPTION_SHOWN_INJECTION_KEY, FORM_ITEM_INJECTION_KEY } from './injectionKeys'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()

const id = useId()
provide(FORM_ITEM_INJECTION_KEY, id)
provide(FORM_DESCRIPTION_SHOWN_INJECTION_KEY, ref(false))
</script>

<template>
  <div :class="cn('flex flex-col gap-2', props.class)">
    <slot />
  </div>
</template>
