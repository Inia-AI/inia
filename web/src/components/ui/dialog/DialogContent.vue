<script setup lang="ts">
import type { DialogContentEmits, DialogContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { X } from '@lucide/vue'
import {
  DialogClose,
  DialogContent,

  DialogOverlay,
  DialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { computed } from 'vue'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<
    DialogContentProps & {
      class?: HTMLAttributes['class']
      overlayClass?: HTMLAttributes['class']
      hideCloseButton?: boolean
    }
  >(),
  { hideCloseButton: false },
)
const emits = defineEmits<DialogContentEmits>()

const delegatedProps = computed(() => {
  const { class: _, overlayClass: __, hideCloseButton: ___, ...delegated } = props

  return delegated
})

const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <DialogPortal>
    <DialogOverlay
      :class="cn('fixed inset-0 z-50 bg-black/80  data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0', props.overlayClass)"
    />
    <DialogContent
      v-bind="{ ...forwarded, ...$attrs }"
      :class="
        cn(
          'dialog-content-modern fixed left-1/2 top-1/2 z-50 grid w-full max-w-lg -translate-x-1/2 -translate-y-1/2 gap-4 border bg-background p-6 shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[state=closed]:slide-out-to-left-1/2 data-[state=closed]:slide-out-to-top-[48%] data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] sm:rounded-lg',
          props.class,
        )"
    >
      <slot />

      <DialogClose
        v-if="!hideCloseButton"
        class="absolute right-4 top-4 rounded-xs text-muted-foreground ring-offset-background transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:pointer-events-none data-[state=open]:bg-accent data-[state=open]:text-muted-foreground"
      >
        <X class="w-4 h-4" />
        <span class="sr-only">Close</span>
      </DialogClose>
    </DialogContent>
  </DialogPortal>
</template>

<style>
.dialog-content-modern {
  background: var(--bg-1) !important;
  border: 1px solid var(--stroke) !important;
  border-radius: var(--radius-panel) !important;
  box-shadow: var(--shadow-panel) !important;
}

.dialog-content-modern button[type="submit"],
.dialog-content-modern button[type="button"]:not(.dialog-close) {
  transition: all 0.15s ease-in-out;
}

.dialog-content-modern button[type="submit"] {
  background: var(--primary) !important;
  color: var(--primary-foreground) !important;
  border: none !important;
  border-radius: var(--radius-button) !important;
  padding: 10px 20px !important;
  font-size: 14px !important;
  font-weight: 600 !important;
}

.dialog-content-modern button[type="submit"]:hover {
  transform: scale(1.02);
  box-shadow: 0 4px 12px rgba(26, 56, 80, 0.3) !important;
}

.dialog-content-modern button[variant="outline"],
.dialog-content-modern button:not([type="submit"]):not(.dialog-close):not([role="checkbox"]):not([role="switch"]) {
  background: transparent !important;
  border: 1px solid var(--stroke) !important;
  color: var(--text-primary) !important;
  border-radius: var(--radius-button) !important;
  padding: 10px 20px !important;
  font-size: 14px !important;
  font-weight: 600 !important;
}

.dialog-content-modern button[variant="outline"]:hover,
.dialog-content-modern button:not([type="submit"]):not(.dialog-close):not([role="checkbox"]):not([role="switch"]):hover {
  background: rgba(255, 255, 255, 0.05) !important;
  border-color: var(--stroke-hover) !important;
}
</style>
