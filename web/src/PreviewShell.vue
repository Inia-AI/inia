<template>
  <DefaultLayout>
    <RouterView />
  </DefaultLayout>
  <AgentActivityRail />
</template>

<script setup lang="ts">
import { RouterView } from 'vue-router'
import { DefaultLayout } from '@/components/layouts'
import AgentActivityRail from '@/components/organisms/AgentActivityRail/AgentActivityRail.vue'
</script>
