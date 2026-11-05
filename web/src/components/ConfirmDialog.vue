<template>
  <AlertDialogRoot :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <AlertDialogPortal>
      <AlertDialogOverlay class="fixed inset-0 bg-black/50 z-40 backdrop-blur-xs" />
      <AlertDialogContent
        class="z-50 fixed left-1/2 top-1/2 w-[90%] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl bg-white p-6 shadow-2xl dark:bg-gray-800"
      >
        <AlertDialogTitle class="text-xl font-semibold text-gray-900 dark:text-white">
          {{ title }}
        </AlertDialogTitle>

        <AlertDialogDescription class="mt-2 text-sm text-gray-600 dark:text-gray-300 leading-relaxed">
          {{ description }}
        </AlertDialogDescription>

        <div class="mt-6 flex justify-end gap-3">
          <button
            type="button"
            class="inline-flex items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 transition dark:bg-gray-700 dark:text-white dark:hover:bg-gray-600"
            @click="emit('update:modelValue', false)"
          >
            {{ cancelLabel || 'Cancel' }}
          </button>

          <button
            type="button"
            class="inline-flex items-center justify-center rounded-md bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 transition"
            @click="handleConfirm"
          >
            {{ confirmLabel || 'Delete' }}
          </button>
        </div>
      </AlertDialogContent>
    </AlertDialogPortal>
  </AlertDialogRoot>
</template>

<script setup lang="ts">
import {
  AlertDialogRoot,
  AlertDialogPortal,
  AlertDialogOverlay,
  AlertDialogContent,
  AlertDialogTitle,
  AlertDialogDescription,
} from 'reka-ui'

defineProps<{
  modelValue: boolean
  title: string
  description: string
  cancelLabel?: string
  confirmLabel?: string
}>()

const emit = defineEmits<{
  (e: 'confirm'): void
  (e: 'update:modelValue', value: boolean): void
}>()

const handleConfirm = () => {
  emit('confirm')
  emit('update:modelValue', false)
}
</script>
