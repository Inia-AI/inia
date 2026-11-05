<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import type { ButtonHTMLAttributes, HTMLAttributes } from 'vue'
import type { ButtonVariants } from '.'
import { cn } from '@/lib/utils'
import { Primitive } from 'reka-ui'
import { buttonVariants } from '.'
import IniaLoader from '../../atoms/IniaLoader/IniaLoader.vue'

interface Props extends PrimitiveProps {
  variant?: ButtonVariants['variant']
  size?: ButtonVariants['size']
  fontSize?: ButtonVariants['fontSize']
  class?: HTMLAttributes['class']
  isLoading?: boolean
  type?: ButtonHTMLAttributes['type']
}

const props = withDefaults(defineProps<Props>(), {
  as: 'button',
  type: 'button',
})
</script>

<template>
  <Primitive
    :as="as"
    :as-child="asChild"
    :type="as === 'button' && !asChild ? type : undefined"
    :disabled="isLoading"
    :class="cn(buttonVariants({ variant, size, fontSize }), 'aria-disabled:pointer-events-none aria-disabled:opacity-50', props.class)"
  >
    <IniaLoader v-if="isLoading" class="w-4" />
    <slot />
  </Primitive>
</template>
