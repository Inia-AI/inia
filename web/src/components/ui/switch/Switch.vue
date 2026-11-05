<script setup lang="ts">
import type { SwitchRootEmits, SwitchRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import {
  SwitchRoot,
  SwitchThumb,
  useForwardPropsEmits,
} from 'reka-ui'
import { computed } from 'vue'

const props = defineProps<SwitchRootProps & { class?: HTMLAttributes['class'] }>()

const emits = defineEmits<SwitchRootEmits>()

const delegatedProps = computed(() => {
  const { class: _, ...delegated } = props

  return delegated
})

const forwarded = useForwardPropsEmits(delegatedProps, emits)

// Note: reka-ui SwitchRoot handles checked state internally via data-state attribute
// which our CSS targets for styling. No need for internal state tracking.
</script>

<template>
  <SwitchRoot
    v-bind="forwarded"
    :class="cn(
      'inia-switch peer inline-flex shrink-0 cursor-pointer items-center rounded-full border-transparent focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:cursor-not-allowed disabled:opacity-50',
      props.class,
    )"
  >
    <SwitchThumb
      :class="cn('inia-switch-thumb pointer-events-none block rounded-full shadow-lg ring-0')"
    >
      <slot name="thumb" />
    </SwitchThumb>
  </SwitchRoot>
</template>

<style>
/* iOS-style toggle switch with high contrast */
button.inia-switch,
button.inia-switch[data-state="unchecked"] {
  background-color: #71717a !important; /* zinc-500 - visible gray */
  height: 24px !important;
  width: 44px !important;
  min-width: 44px !important;
  border: none !important;
  border-radius: 12px !important;
  padding: 0 !important;
  position: relative !important;
  transition: background-color 0.2s ease !important;
}

/* Green when checked */
button.inia-switch[data-state="checked"] {
  background-color: #22c55e !important; /* green-500 */
}

button.inia-switch[data-state="checked"]:hover {
  background-color: #16a34a !important; /* green-600 */
}

button.inia-switch[data-state="unchecked"]:hover {
  background-color: #52525b !important; /* zinc-600 */
}

/* Thumb - always white for contrast */
button.inia-switch .inia-switch-thumb,
button.inia-switch span.inia-switch-thumb {
  background-color: var(--bg-1) !important;
  height: 20px !important;
  width: 20px !important;
  min-width: 20px !important;
  border-radius: 10px !important;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.4) !important;
  display: block !important;
  transition: transform 0.2s ease !important;
  position: absolute !important;
  top: 2px !important;
  left: 2px !important;
  transform: translateX(0) !important;
}

button.inia-switch[data-state="checked"] .inia-switch-thumb {
  transform: translateX(20px) !important;
}
</style>
