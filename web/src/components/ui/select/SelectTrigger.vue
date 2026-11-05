<script setup lang="ts">
import type { SelectTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { ChevronDown } from '@lucide/vue'
import { SelectIcon, SelectTrigger, useForwardProps } from 'reka-ui'
import { computed } from 'vue'
import { selectTriggerClass } from './triggerClass'

const props = defineProps<SelectTriggerProps & { class?: HTMLAttributes['class'] }>()

const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props

  return delegated
})

const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <SelectTrigger
    v-bind="forwardedProps"
    :class="cn(selectTriggerClass, props.class)"
  >
    <slot />
    <SelectIcon as-child>
      <ChevronDown class="w-4 h-4 text-muted-foreground shrink-0" />
    </SelectIcon>
  </SelectTrigger>
</template>
