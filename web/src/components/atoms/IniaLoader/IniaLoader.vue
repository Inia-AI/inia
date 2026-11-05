<script setup lang="ts">
import IniaIcon from '../IniaIcon/IniaIcon.vue'
</script>

<template>
  <IniaIcon name="spinner" class="animate-spin" />
</template>

<style scoped>
@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.animate-spin {
  animation: spin 1s linear infinite;
}
</style>
