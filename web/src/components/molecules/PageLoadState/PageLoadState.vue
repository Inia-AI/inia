<script setup lang="ts">
import { IniaLoader } from '@/components/atoms'

defineProps<{ title: string; loading?: boolean }>()
</script>

<template>
  <h1 class="sr-only">{{ title }}</h1>
  <div v-if="loading" class="flex justify-center py-16"><IniaLoader class="w-8" /></div>
  <slot v-else />
</template>
