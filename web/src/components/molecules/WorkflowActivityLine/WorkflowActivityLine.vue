<template>
  <div class="activity" aria-live="polite">
    <TransitionGroup name="frontier" tag="ul" class="frontier">
      <li
        v-for="line in visibleLines"
        :key="line.label"
        class="frontier-line"
        :class="{ 'is-done': line.passedAt !== null }"
      >
        <IniaIcon
          v-if="line.passedAt !== null"
          class="frontier-tick text-success"
          name="circle-check"
          aria-hidden="true"
        />
        <AgentPresenceDot v-else class="frontier-dot" :fresh="freshnessFor(line)" />
        <span class="frontier-voice">{{ line.label }}</span>
      </li>
    </TransitionGroup>
    <p class="activity-byline">
      <button
        v-if="hiddenCount > 0 || expanded"
        type="button"
        class="frontier-toggle"
        @click="expanded = !expanded"
      >
        {{
          expanded
            ? $t('shared.agentActivity.classicShowFewer')
            : $t('shared.agentActivity.classicMore', { count: hiddenCount })
        }}
      </button>
      <span v-if="hiddenCount > 0 || expanded"> · </span>
      {{
        $t('shared.agentActivity.classicProgress', {
          done: entry.doneCount,
          total: entry.stepCount,
        })
      }}
    </p>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { freshnessOf } from '@/lib/agentPresence'
import type { WorkflowActivityView } from '@/lib/workflowActivity'
import AgentPresenceDot from '@/components/atoms/AgentPresenceDot/AgentPresenceDot.vue'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'

const MAX_COLLAPSED_LINES = 3
const CLOCK_TICK_MS = 1000
const PASSED_LINE_LINGER_MS = 12_000

interface FrontierLine {
  label: string
  enteredAt: number
  passedAt: number | null
}

const props = defineProps<{
  entry: WorkflowActivityView
  watchedFromStart?: boolean
}>()

const { t: $t } = useI18n()

const lines = ref<FrontierLine[]>([])
const seenDone = ref(new Set<string>())
const primed = ref(false)
const expanded = ref(false)

watch(
  () => props.entry,
  entry => {
    const at = Date.now()
    const isFirstSight = !primed.value
    primed.value = true

    const kept: FrontierLine[] = []
    for (const line of lines.value) {
      if (entry.activeLabels.includes(line.label)) {
        kept.push(line.passedAt === null ? line : { ...line, passedAt: null })
      } else if (entry.doneLabels.includes(line.label)) {
        const passedAt = line.passedAt ?? at
        if (at - passedAt < PASSED_LINE_LINGER_MS) {
          kept.push({ ...line, passedAt })
        }
      }
    }

    const arrivingActive = entry.activeLabels
      .filter(label => !kept.some(line => line.label === label))
      .map(label => ({ label, enteredAt: at, passedAt: null }))

    const arrivingPassed = isFirstSight && !props.watchedFromStart
      ? []
      : entry.doneLabels
          .filter(label => !seenDone.value.has(label) && !kept.some(line => line.label === label))
          .map(label => ({ label, enteredAt: at, passedAt: at }))

    seenDone.value = new Set(entry.doneLabels)
    lines.value = [...arrivingActive, ...arrivingPassed, ...kept]
  },
  { immediate: true, deep: true },
)

const now = ref(Date.now())
let clockTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  clockTimer = setInterval(() => {
    now.value = Date.now()
  }, CLOCK_TICK_MS)
})

onUnmounted(() => {
  if (clockTimer !== null) {
    clearInterval(clockTimer)
  }
})

const livingLines = computed(() =>
  lines.value.filter(
    line => line.passedAt === null || now.value - line.passedAt < PASSED_LINE_LINGER_MS,
  ),
)

const activeZone = computed(() => livingLines.value.filter(line => line.passedAt === null))

const passedZone = computed(() =>
  [...livingLines.value.filter(line => line.passedAt !== null)].sort(
    (a, b) => (b.passedAt ?? 0) - (a.passedAt ?? 0) || a.enteredAt - b.enteredAt,
  ),
)

const visibleLines = computed(() => {
  if (expanded.value) {
    return [...activeZone.value, ...passedZone.value]
  }
  const shownActive = activeZone.value.slice(0, MAX_COLLAPSED_LINES)
  return [
    ...shownActive,
    ...passedZone.value.slice(0, MAX_COLLAPSED_LINES - shownActive.length),
  ]
})

const hiddenCount = computed(() => livingLines.value.length - visibleLines.value.length)

const freshnessFor = (line: FrontierLine): number => freshnessOf(line.enteredAt, now.value)
</script>

<style scoped>
.activity {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  margin: 0;
}

.frontier {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.frontier-line {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 1.35em;
}

.frontier-dot {
  color: var(--presence);
}

.frontier-tick {
  font-size: 0.7rem;
}

.frontier-voice {
  color: var(--text-primary);
  font-size: 0.875rem;
  line-height: 1.35;
}

.frontier-line.is-done .frontier-voice {
  color: var(--text-secondary);
}

.activity-byline {
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.75rem;
}

.frontier-toggle {
  padding: 0;
  border: none;
  background: none;
  color: inherit;
  font: inherit;
  text-decoration: underline;
  cursor: pointer;
}

.frontier-toggle:focus-visible {
  outline: 2px solid var(--presence);
  outline-offset: 2px;
}

.frontier-enter-active {
  transition:
    opacity 0.4s ease,
    transform 0.4s ease;
}

.frontier-leave-active {
  position: absolute;
  width: 100%;
  transition: opacity 0.2s ease;
}

.frontier-move {
  transition: transform 0.4s ease;
}

.frontier-enter-from {
  opacity: 0;
  transform: translateY(-0.2rem);
}

.frontier-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .frontier-enter-active,
  .frontier-leave-active,
  .frontier-move {
    transition: none;
  }

  .frontier-enter-from {
    transform: none;
  }
}
</style>
