<template>
  <Teleport to="body">
    <Transition name="agent-panel-fade">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions, vue/no-restricted-syntax -- backdrop only: Escape closes the panel -->
      <div
        v-if="entry"
        class="agent-panel-overlay"
        @click.self="store.closePanel()"
      >
        <FocusScope as-child trapped loop>
          <aside
            class="agent-panel"
            role="dialog"
            aria-modal="true"
            :aria-label="entry.blockName"
          >
            <header class="agent-panel-header">
              <div class="agent-panel-heading">
                <span class="agent-panel-block">{{ entry.blockName }}</span>
                <span class="agent-panel-workflow">
                  <template v-if="entry.round > FIRST_ROUND"
                    >{{
                      $t('shared.agentActivity.round', { round: entry.round })
                    }}
                    ·
                  </template>
                  {{ entry.counterpartyName || entry.executionTitle }}
                </span>
              </div>
              <div class="agent-panel-state">
                <AgentPresenceDot
                  v-if="!entry.settled"
                  class="agent-panel-live-dot"
                  :fresh="gapFresh"
                />
                <IniaIcon
                  v-else
                  :name="isFailed ? 'circle-exclamation' : 'circle-check'"
                  :class="isFailed ? 'text-error' : 'text-success'"
                />
                <button
                  type="button"
                  class="agent-panel-close"
                  :aria-label="$t('shared.agentActivity.close')"
                  @click="store.closePanel()"
                >
                  <IniaIcon name="xmark" />
                </button>
              </div>
            </header>

            <div v-if="phases.length > 0" class="agent-panel-phases">
              <div class="agent-panel-phasebar">
                <span
                  v-for="(phase, index) in phases"
                  :key="phase.label"
                  class="agent-panel-phase"
                  :class="{
                    done: index < currentPhaseIndex,
                    current: index === currentPhaseIndex,
                  }"
                  :title="phase.label"
                />
              </div>
              <span class="agent-panel-phase-label">
                {{ phases[currentPhaseIndex]?.label }}
              </span>
            </div>

            <div ref="feedElement" class="agent-panel-feed">
              <p v-if="beats.length === 0" class="agent-panel-waiting">
                {{ $t('shared.agentActivity.waiting') }}
              </p>
              <div
                v-for="{ beat, key, repeat } in keyedBeats"
                :key="key"
                class="agent-beat"
                :class="{
                  hero: beat.kind === 'Hero',
                  check: beat.kind === 'Check',
                }"
              >
                <span class="agent-beat-time">{{
                  formatOffset(beat.atSeconds)
                }}</span>
                <span class="agent-beat-body">
                  <span class="agent-beat-label">
                    <template v-if="beat.kind === 'Check'"
                      >✓ “{{ beat.label }}”</template
                    >
                    <template v-else>{{ beat.label }}</template>
                    <span v-if="repeat > 1" class="agent-beat-repeat"
                      >×{{ repeat }}</span
                    >
                  </span>
                  <span v-if="beat.detail" class="agent-beat-detail">{{
                    beat.detail
                  }}</span>
                </span>
              </div>
              <div v-if="!entry.settled" class="agent-panel-gap">
                <AgentPresenceDot class="agent-panel-gap-dot" :fresh="gapFresh" />
                <span v-if="gapLabel">{{ gapLabel }}</span>
                <span v-if="gapElapsed" class="agent-panel-gap-elapsed">{{
                  gapElapsed
                }}</span>
              </div>
            </div>

            <footer v-if="entry.settled && receipt" class="agent-panel-receipt">
              <div class="agent-panel-receipt-title">
                {{ $t('shared.agentActivity.receipt.title') }}
              </div>
              <dl class="agent-panel-receipt-grid">
                <template v-if="receipt.durationSeconds != null">
                  <dt>{{ $t('shared.agentActivity.receipt.duration') }}</dt>
                  <dd>{{ formatOffset(receipt.durationSeconds) }}</dd>
                </template>
                <template v-if="receipt.documentNames.length > 0">
                  <dt>{{ $t('shared.agentActivity.receipt.documents') }}</dt>
                  <dd>
                    <ul class="agent-panel-receipt-docs">
                      <li v-for="name in receipt.documentNames" :key="name">
                        {{ name }}
                      </li>
                    </ul>
                  </dd>
                </template>
              </dl>
            </footer>
          </aside>
        </FocusScope>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { FocusScope } from 'reka-ui'
import API from '@/api'
import { collapseRepeatedBeats, freshnessOf } from '@/lib/agentPresence'
import useAgentActivityStore, { FIRST_ROUND } from '@/store/agentActivity'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import AgentPresenceDot from '@/components/atoms/AgentPresenceDot/AgentPresenceDot.vue'

const { t: $t } = useI18n()
const store = useAgentActivityStore()

const feedElement = ref<HTMLElement | null>(null)
const fetchedFinalKeys = new Set<string>()

const entry = computed(() => store.panelEntry)
const digest = computed(() => entry.value?.displayDigest ?? null)
const beats = computed(() => digest.value?.beats ?? [])
const keyedBeats = computed(() => collapseRepeatedBeats(beats.value))
const phases = computed(() => digest.value?.phases ?? [])
const receipt = computed(() => digest.value?.receipt ?? null)
const isFailed = computed(() => digest.value?.outcome === 'Failed')

const currentPhaseIndex = computed(() => {
  const lastBeat = beats.value[beats.value.length - 1]
  return lastBeat?.phaseIndex ?? 0
})

const gapLabel = computed(() => {
  const phaseGapLabel = phases.value[currentPhaseIndex.value]?.gapLabel
  if (phaseGapLabel) {
    return phaseGapLabel
  }
  return beats.value.length > 0 ? null : $t('shared.agentActivity.waiting')
})

const lastBeatArrivedAt = ref(Date.now())
const clockNow = ref(Date.now())
let gapTimer: ReturnType<typeof setInterval> | null = null

const gapElapsed = computed(() => {
  const elapsedMs = clockNow.value - lastBeatArrivedAt.value
  if (elapsedMs < 60_000) return null
  return `${Math.floor(elapsedMs / 60_000)}m`
})

const gapFresh = computed(() =>
  freshnessOf(lastBeatArrivedAt.value, clockNow.value),
)

const formatOffset = (seconds: number) => {
  const minutes = Math.floor(seconds / 60)
  const rest = `${seconds % 60}`.padStart(2, '0')
  return `${minutes}:${rest}`
}

watch(
  () => [entry.value?.key, entry.value?.settled] as const,
  async ([key, settled]) => {
    if (!key || !settled || entry.value?.finalDigest) return
    if (fetchedFinalKeys.has(key)) return
    fetchedFinalKeys.add(key)
    try {
      const blockId = entry.value?.blockId
      if (!blockId) return
      const final = await API.AgentActivity.service.getBlockActivity(
        blockId,
        entry.value?.round,
      )
      store.setFinalDigest(key, final)
    } catch {
      fetchedFinalKeys.delete(key)
    }
  },
  { immediate: true },
)

watch(
  () => beats.value.length,
  async () => {
    lastBeatArrivedAt.value = Date.now()
    await nextTick()
    if (feedElement.value) {
      feedElement.value.scrollTop = feedElement.value.scrollHeight
    }
  },
)

const handleKeyDown = (event: KeyboardEvent) => {
  if (event.key === 'Escape' && entry.value) {
    store.closePanel()
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleKeyDown)
  gapTimer = setInterval(() => {
    clockNow.value = Date.now()
  }, 5_000)
})
onUnmounted(() => {
  document.removeEventListener('keydown', handleKeyDown)
  if (gapTimer) clearInterval(gapTimer)
})
</script>

<style scoped>
.agent-panel-overlay {
  position: fixed;
  inset: 0;
  z-index: 60;
  background: rgb(0 0 0 / 35%);
  display: flex;
  justify-content: flex-end;
}

.agent-panel {
  width: min(480px, 100vw);
  height: 100%;
  display: flex;
  flex-direction: column;
  background: var(--bg-1);
  border-left: 1px solid var(--stroke);
  box-shadow: var(--shadow-card);
}

.agent-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  padding: var(--spacing-md);
  border-bottom: 1px solid var(--stroke);
}

.agent-panel-heading {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.agent-panel-block {
  color: var(--text-primary);
  font-weight: 600;
}

.agent-panel-workflow {
  color: var(--text-secondary);
  font-size: 0.85rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.agent-panel-state {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.agent-panel-close {
  color: var(--text-secondary);
  padding: var(--spacing-xs);
}

.agent-panel-close:hover {
  color: var(--text-primary);
}

.agent-panel-phases {
  padding: var(--spacing-sm) var(--spacing-md);
  border-bottom: 1px solid var(--stroke);
}

.agent-panel-phasebar {
  display: flex;
  gap: 4px;
}

.agent-panel-phase {
  flex: 1;
  height: 4px;
  border-radius: 2px;
  background: var(--stroke);
}

.agent-panel-phase.done {
  background: var(--accent-success);
}

.agent-panel-phase.current {
  background: var(--accent-warning);
}

.agent-panel-phase-label {
  display: inline-block;
  margin-top: var(--spacing-xs);
  color: var(--text-secondary);
  font-size: 0.8rem;
}

.agent-panel-feed {
  flex: 1;
  overflow-y: auto;
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.agent-panel-waiting {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.agent-beat {
  display: flex;
  gap: var(--spacing-sm);
  padding: var(--spacing-xs) var(--spacing-sm);
}

.agent-beat-repeat {
  margin-left: 6px;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
  font-size: 0.85em;
}

.agent-beat.hero {
  background: var(--accent-warning-light);
  border-radius: var(--radius-button);
}

.agent-beat.check .agent-beat-label {
  color: var(--accent-success);
}

.agent-beat-time {
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
  font-size: 0.8rem;
  min-width: 2.5rem;
}

.agent-beat-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.agent-beat-label {
  color: var(--text-primary);
  font-size: 0.9rem;
}

.agent-beat-detail {
  color: var(--text-secondary);
  font-size: 0.8rem;
  overflow-wrap: anywhere;
}

.agent-panel-gap {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  color: var(--text-secondary);
  font-size: 0.85rem;
  padding: var(--spacing-xs) var(--spacing-sm);
}

.agent-panel-live-dot,
.agent-panel-gap-dot {
  color: var(--presence);
}

.agent-panel-gap-elapsed {
  font-variant-numeric: tabular-nums;
}

.agent-panel-receipt {
  border-top: 1px solid var(--stroke);
  padding: var(--spacing-md);
}

.agent-panel-receipt-title {
  color: var(--text-primary);
  font-weight: 600;
  margin-bottom: var(--spacing-sm);
}

.agent-panel-receipt-docs {
  /* One document per line: these are long filenames, and a comma-joined run
     of them is unreadable at this width. */
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.agent-panel-receipt-docs li {
  overflow-wrap: anywhere;
}

.agent-panel-receipt-grid {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
}

.agent-panel-receipt-grid dt {
  color: var(--text-secondary);
  font-size: 0.85rem;
}

.agent-panel-receipt-grid dd {
  color: var(--text-primary);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.agent-panel-fade-enter-active,
.agent-panel-fade-leave-active {
  transition: opacity 0.15s ease;
}

.agent-panel-fade-enter-from,
.agent-panel-fade-leave-to {
  opacity: 0;
}

@media (max-width: 768px) {
  .agent-panel {
    width: 100vw;
  }
}
</style>
