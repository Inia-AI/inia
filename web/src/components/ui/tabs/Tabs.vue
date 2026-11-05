<script setup lang="ts">
import type { TabsRootEmits, TabsRootProps } from 'reka-ui'
import { TabsRoot, useForwardPropsEmits } from 'reka-ui'

type TabValue = string | number
const props = defineProps<TabsRootProps<TabValue>>()
const emits = defineEmits<TabsRootEmits<TabValue>>()

const forwarded = useForwardPropsEmits(props, emits)
</script>

<template>
  <TabsRoot v-bind="forwarded">
    <slot />
  </TabsRoot>
</template>
