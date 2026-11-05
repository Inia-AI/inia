<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'

const route = useRoute()
</script>

<template>
  <div class="page-view">
    <div class="page-container not-found">
      <h1 class="page-title">{{ $t('page.notFound.title') }}</h1>
      <p class="page-subtitle">
        {{ $t('page.notFound.description') }} <code class="not-found-address">{{ route.fullPath }}</code>
      </p>
      <Button as-child class="btn-gradient-primary">
        <RouterLink to="/">{{ $t('page.notFound.home') }}</RouterLink>
      </Button>
    </div>
  </div>
</template>

<style scoped>
.not-found {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-md);
  padding-top: var(--spacing-xl);
}

.not-found-address {
  font-family: var(--font-mono, monospace);
  overflow-wrap: anywhere;
}
</style>
