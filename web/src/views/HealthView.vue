<template>
  <div class="p-4">
    <h1 class="text-xl mb-4">System Health Status</h1>
    <div v-if="isLoading" class="text-muted-foreground">
      <span class="animate-pulse">Checking system health...</span>
    </div>
    <div v-else-if="error" class="text-error">
      <div class="font-bold">Service Unhealthy</div>
      <div class="text-sm">{{ error.message }}</div>
    </div>
    <div v-else class="text-success">
      <div class="font-bold">Service Healthy</div>
      <pre class="text-sm mt-2 bg-gray-100 p-2 rounded-sm">{{ JSON.stringify(data, null, 2) }}</pre>
    </div>
  </div>
</template>

<script setup lang="ts">
import API from '@/api'

const { isLoading, data, error } = API.Health.useHealth()
</script>
