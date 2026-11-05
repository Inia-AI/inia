<template>
  <span class="presence-dot" :style="{ '--fresh': fresh }" aria-hidden="true">
    <span class="presence-dot-core" />
    <span class="presence-dot-ring" />
  </span>
</template>

<script setup lang="ts">
defineProps<{ fresh: number }>()
</script>

<style scoped>
.presence-dot {
  position: relative;
  flex-shrink: 0;
  width: 8px;
  height: 8px;
}

.presence-dot-core {
  position: absolute;
  inset: 1.5px;
  border-radius: 50%;
  background: currentColor;
  opacity: calc(0.35 + 0.65 * var(--fresh, 0));
  transition: opacity 1s linear;
}

/* The ring's amplitude IS the freshness signal: five quiet minutes decay it
   to invisible, so a run that dies goes still without any death detection. */
.presence-dot-ring {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  border: 1px solid currentColor;
  opacity: 0;
  animation: presence-ping 2.4s ease-out infinite;
}

@keyframes presence-ping {
  0% {
    transform: scale(0.55);
    opacity: calc(0.85 * var(--fresh, 0));
  }

  70%,
  100% {
    transform: scale(2.5);
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .presence-dot-ring {
    animation: none;
  }
}
</style>
