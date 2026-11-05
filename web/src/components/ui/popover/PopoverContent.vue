<script setup lang="ts">
import type { PopoverContentEmits, PopoverContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import {
  injectDialogRootContext,
  injectPopoverRootContext,
  PopoverContent,

  PopoverPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { computed, nextTick } from 'vue'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<PopoverContentProps & {
    class?: HTMLAttributes['class']
    /**
     * Override for where the content renders. Inside a dialog it defaults to rendering in
     * place: the dialog's focus trap treats teleported content as outside and yanks focus
     * back, so a search input in a teleported popover can never be typed in. Everywhere
     * else it defaults to teleporting to the body as usual.
     */
    portalDisabled?: boolean
  }>(),
  {
    align: 'center',
    sideOffset: 4,
    portalDisabled: undefined,
  },
)
const emits = defineEmits<PopoverContentEmits>()

const insideDialog = injectDialogRootContext(null) !== null

const delegatedProps = computed(() => {
  const { class: _, portalDisabled: __, ...delegated } = props

  return delegated
})

const forwarded = useForwardPropsEmits(delegatedProps, emits)

const popoverRoot = injectPopoverRootContext()

// reka focuses the first tabbable control in the content, or else a wrapper that cannot take
// focus, so content with no control of its own would leave focus behind on the trigger.
const focusContentWithoutControls = async (event: Event) => {
  const container = event.target as HTMLElement
  await nextTick()
  if (event.defaultPrevented || container.contains(document.activeElement)) return
  const dialog = container.matches('[role="dialog"]') ? container : container.querySelector<HTMLElement>('[role="dialog"]')
  dialog?.focus()
}

// An always-open Command in the content is a dismissable layer of its own, and the topmost one,
// so reka hands Escape to it and the popover never hears the key. Every reka layer listens for
// Escape on window, and the popover unmounts before that listener runs, so a dialog under the
// popover would take the same key as its own and close with it.
const closeOnEscape = (event: KeyboardEvent) => {
  if (event.key === 'Escape') {
    event.stopPropagation()
    popoverRoot.onOpenChange(false)
  }
}
</script>

<template>
  <PopoverPortal :disabled="portalDisabled ?? insideDialog">
    <PopoverContent
      v-bind="{ ...forwarded, ...$attrs }"
      tabindex="-1"
      @open-auto-focus="focusContentWithoutControls"
      @keydown="closeOnEscape"
      :class="
        cn(
          'z-50 w-72 rounded-md border bg-popover p-4 text-popover-foreground shadow-md outline-hidden data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2',
          props.class,
        )
      "
    >
      <slot />
    </PopoverContent>
  </PopoverPortal>
</template>
