<script setup lang="ts">
import type { AccordionTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { ChevronDown } from '@lucide/vue'
import {
  AccordionHeader,
  AccordionTrigger,

} from 'reka-ui'
import { computed } from 'vue'

const props = defineProps<
  AccordionTriggerProps & { class?: HTMLAttributes['class'], ariaLabel?: string, headingLevel: 2 | 3 | 4 | 5 | 6 }
>()

const delegatedProps = computed(() => {
  const { class: _, ariaLabel: __, headingLevel: ___, ...delegated } = props

  return delegated
})
</script>

<template>
  <AccordionHeader class="flex" :as="`h${props.headingLevel}`">
    <AccordionTrigger
      v-bind="delegatedProps"
      :aria-label="props.ariaLabel"
      :class="
        cn(
          'flex flex-1 items-center justify-between py-4 font-medium transition-all hover:underline [&[data-state=open]>svg]:rotate-180',
          props.class,
        )
      "
    >
      <slot />
      <slot name="icon">
        <ChevronDown
          class="h-4 w-4 shrink-0 transition-transform duration-200"
        />
      </slot>
    </AccordionTrigger>
  </AccordionHeader>
</template>
