<script setup lang="ts">
import { useRouter } from 'vue-router'
import { Button } from '@/components/ui/button'
import { signOut } from '@/lib/signOut'
import useThemeStore from '@/store/theme'

const router = useRouter()
// A blank-layout page has no theme toggle, and creating the store is what applies the saved theme.
useThemeStore()

const onSignOut = () => {
  signOut()
  router.push({ name: 'Login' })
}
</script>

<template>
  <div class="page-view no-organization-view">
    <div class="page-container no-organization">
      <h1 class="page-title">{{ $t('page.noOrganization.title') }}</h1>
      <p class="page-subtitle">{{ $t('page.noOrganization.description') }}</p>
      <Button class="btn-gradient-primary" @click="onSignOut">{{ $t('page.noOrganization.signOut') }}</Button>
    </div>
  </div>
</template>

<style scoped>
.no-organization-view {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}

.no-organization {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-md);
  max-width: 36rem;
}
</style>
