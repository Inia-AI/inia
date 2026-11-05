<script setup lang="ts">
import { computed } from 'vue'
import { useLiveDuration } from '@/composables/useLiveDuration'
import type { WorkflowState } from '@/enums'

const props = defineProps<{
  startedAt?: string | null
  duration?: string | null
  workflowState?: WorkflowState
  fallback?: string
}>()

const source = computed(() => ({
  startedAt: props.startedAt ?? null,
  duration: props.duration ?? null,
  workflowState: props.workflowState,
}))

const value = useLiveDuration(source)
const display = computed(() => value.value ?? props.fallback ?? '-')
</script>

<template>
  <span>{{ display }}</span>
</template>
