<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { Button } from '@/components/ui/button'
import { NEW_WORK, type Capability } from '@/constants/capabilities'
import { extensionPoints } from '@/modules/extensionPoints'

const route = useRoute()
const capability = computed(() => {
  void route.fullPath
  return window.history.state?.capability as Capability | undefined
})
const isNewWork = computed(() => !!capability.value && NEW_WORK.includes(capability.value))
</script>

<template>
  <div class="page-view">
    <div class="page-container not-included">
      <h1 class="page-title">{{ $t('page.notIncluded.title') }}</h1>
      <p class="page-subtitle">
        {{ isNewWork ? $t('page.notIncluded.newWork') : $t('page.notIncluded.description') }}
      </p>
      <component :is="piece" v-for="(piece, index) in extensionPoints.notIncludedActions" :key="index" :capability="capability" />
      <Button as-child class="btn-gradient-primary">
        <RouterLink to="/">{{ $t('page.notIncluded.home') }}</RouterLink>
      </Button>
    </div>
  </div>
</template>

<style scoped>
.not-included {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-md);
  padding-top: var(--spacing-xl);
}
</style>
