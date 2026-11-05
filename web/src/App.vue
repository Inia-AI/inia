<template>
  <div v-if="isLoading" class="w-full h-full flex justify-center">
    <IniaLoader class="w-24" />
  </div>
  <template v-else>
    <DefaultLayout v-if="layout == 'default'">
      <RouterView />
    </DefaultLayout>

    <BlankLayout v-else-if="layout == 'blank'">
      <RouterView />
    </BlankLayout>

    <RouterView v-else />
  </template>

  <Toaster />
  <SourceChainPanel />
  <!-- The rail polls the reports API, so it only exists where a session can exist:
       never on public routes (login and friends) and never before the router has
       resolved the first route — both fire unauthenticated 403s otherwise. -->
  <AgentActivityRailIfIncluded v-if="!isLoading && !route.meta.isPublic" />
</template>

<script setup lang="ts">
import { RouterView } from 'vue-router'
import { DefaultLayout, BlankLayout } from './components/layouts'
import { SourceChainPanel, AgentActivityRail } from './components/organisms'
import { useRouter, useRoute } from 'vue-router'
import { ref, watch } from 'vue'
import Toaster from '@/components/ui/toast/Toaster.vue'
import { useColorMode } from '@vueuse/core'
import { useRefreshToken } from '@/composables/refreshToken'
import { withCapability } from '@/components/capabilities/withCapability'
import { CAPABILITIES } from '@/constants/capabilities'

const AgentActivityRailIfIncluded = withCapability(CAPABILITIES.RUN_LOGS, AgentActivityRail)

const mode = useColorMode()

mode.value = 'dark'

const isSelf = window.top === window.self

const isLoading = ref(true)
const layout = ref<string>('')

const router = useRouter()
const route = useRoute()
const { setRefreshTokenTimer } = useRefreshToken()

router.isReady().then(() => {
  isLoading.value = false
})

watch(route, () => {
  const queryLayout = router?.currentRoute?.value?.query?.layout?.toString()
  const metaLayout = router?.currentRoute?.value?.meta?.layout?.toString()

  if (isSelf) {
    layout.value = queryLayout || metaLayout || 'default'
  }
})

setRefreshTokenTimer()
</script>
