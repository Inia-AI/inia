<template>
  <font-awesome-icon :class="classes" :icon="`fa-${type} fa-${name}`" />
</template>

<script setup lang="ts">
defineProps({
  type: {
    type: String,
    default: 'solid',
  },
  name: {
    type: String,
  },
  classes: {
    type: String,
    default: 'cursor-pointer',
  },
})
</script>
