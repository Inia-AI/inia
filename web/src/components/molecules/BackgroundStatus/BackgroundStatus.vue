<script setup lang="ts">
import { BackgroundJobState } from '@/enums'
import { computed } from 'vue'

const props = defineProps({
  status: {
    type: String,
  },
  fontSize: {
    type: String,
    default: 'text-lg',
  },
})

const icon = computed(() => {
  switch (props.status) {
    case BackgroundJobState.Done:
      return {
        name: 'circle-check',
        color: 'text-success',
      }
    case BackgroundJobState.Error:
      return {
        name: 'circle-exclamation',
        color: 'text-error',
      }
    case BackgroundJobState.Processing:
      return {
        name: 'spinner',
        color: 'text-warning',
      }
    default:
      return null
  }
})
</script>

<template>
  <IniaIcon
    v-if="icon"
    :name="icon.name"
    :class="[props.fontSize, icon.color]"
  />
</template>
