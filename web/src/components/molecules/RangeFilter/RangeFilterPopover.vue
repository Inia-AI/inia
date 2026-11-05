<script setup lang="ts">
import { ChevronDown, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { selectTriggerClass } from '@/components/ui/select/triggerClass'
import { cn } from '@/lib/utils'
import { useId } from 'vue'

defineProps<{
  label: string
  active: boolean
  clearLabel: string
  ariaLabel?: string
}>()

const open = defineModel<boolean>('open', { default: false })

const purposeId = useId()
const labelId = useId()

const emit = defineEmits<{
  (e: 'clear'): void
}>()
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <button
        type="button"
        :aria-labelledby="ariaLabel ? `${purposeId} ${labelId}` : undefined"
        :title="label"
        :class="cn(selectTriggerClass, active && 'font-medium')"
      >
        <span v-if="ariaLabel" :id="purposeId" class="sr-only">{{ ariaLabel }}</span>
        <span :id="labelId">{{ label }}</span>
        <ChevronDown class="h-4 w-4 text-muted-foreground shrink-0" />
      </button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-auto p-0">
      <slot />
      <div v-if="active" class="flex justify-end border-t border-border p-2">
        <Button variant="ghost" size="sm" @click="emit('clear')">
          <X class="h-4 w-4" />
          {{ clearLabel }}
        </Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
