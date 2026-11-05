<template>
  <button
    type="button"
    class="presence"
    :class="[
      `is-${state}`,
      {
        'is-hero': latestKind === 'Hero',
      },
    ]"
    :aria-label="label"
    @click="emit('open')"
  >
    <span
      :key="voice"
      class="presence-voice"
      :class="{ 'is-check': latestKind === 'Check' }"
    >
      <template v-if="latestKind === 'Check'">
        <span class="voice-tick" aria-hidden="true">✓</span>
        <span class="voice-quote" aria-hidden="true">“</span>{{ voice
        }}<span class="voice-quote" aria-hidden="true">”</span>
      </template>
      <template v-else>{{ voice }}</template>
    </span>
    <span class="presence-byline">
      <AgentPresenceDot
        v-if="state === 'working'"
        class="byline-dot"
        :fresh="fresh"
      />
      <IniaIcon
        v-else
        class="byline-glyph"
        :name="state === 'failed' ? 'circle-exclamation' : 'circle-check'"
        :class="state === 'failed' ? 'text-error' : 'text-success'"
      />
      <span class="byline-text">
        <span class="presence-role">{{ roleLabel }}</span>
        <span v-if="tail" class="presence-tail">· {{ tail }}</span>
      </span>
    </span>
    <IniaIcon class="presence-caret" name="expand" aria-hidden="true" />
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { freshnessOf } from '@/lib/agentPresence'
import { FIRST_ROUND, type AgentRailEntry } from '@/store/agentActivity'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import AgentPresenceDot from '@/components/atoms/AgentPresenceDot/AgentPresenceDot.vue'

const JUST_NOW_SECONDS = 45

const props = defineProps<{
  entry: AgentRailEntry
  now: number
  fileName?: string
}>()

const emit = defineEmits<{ open: [] }>()

const { t: $t } = useI18n()

type AgentPresenceState = 'working' | 'done' | 'failed'

const state = computed<AgentPresenceState>(() => {
  if (props.entry.displayDigest?.outcome === 'Failed') {
    return 'failed'
  }
  return props.entry.settled ? 'done' : 'working'
})

const beats = computed(() => props.entry.displayDigest?.beats ?? [])

const latestKind = computed(() => beats.value.at(-1)?.kind)

const fresh = computed(() => {
  if (state.value !== 'working') {
    return 0
  }
  return freshnessOf(props.entry.lastBeatAt, props.now)
})

const voice = computed(() => {
  const lastBeat = beats.value.at(-1)
  if (state.value === 'failed') {
    return lastBeat
      ? $t('shared.agentActivity.failed')
      : $t('shared.agentActivity.couldNotStart')
  }
  if (lastBeat) {
    return lastBeat.label
  }
  return state.value === 'done'
    ? $t('shared.agentActivity.finished')
    : $t('shared.agentActivity.atWork')
})

const spanWord = (totalSeconds: number): string => {
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  if (minutes === 0) {
    return `${seconds}s`
  }
  return `${minutes}m ${String(seconds).padStart(2, '0')}s`
}

const updatedAgo = computed(() => {
  if (beats.value.length === 0) {
    return null
  }
  const quietSeconds = Math.max(
    0,
    Math.floor((props.now - props.entry.lastBeatAt) / 1000),
  )
  if (quietSeconds < JUST_NOW_SECONDS) {
    return $t('shared.agentActivity.updatedJustNow')
  }
  return $t('shared.agentActivity.updatedAgo', {
    minutes: Math.max(1, Math.floor(quietSeconds / 60)),
  })
})

// Blocks get named for what they do, so a name whose last word is already a role noun would
// double it. Either noun, since a worker can be named "Statement verifier" too.
const roleLabel = computed(() => {
  const name = props.entry.blockName.trim()
  const verifying = props.entry.role === 'verifier'
  const nouns = [
    $t('shared.agentActivity.roleNounAgent'),
    $t('shared.agentActivity.roleNounVerifier'),
  ].map(noun => noun.toLowerCase())
  const lastWord = name.split(/\s+/).at(-1)?.toLowerCase() ?? ''
  if (name.length === 0 || nouns.includes(lastWord)) {
    return name
  }
  return $t(
    verifying ? 'shared.agentActivity.verifier' : 'shared.agentActivity.agent',
    { name },
  )
})

const tail = computed(() => {
  const parts: string[] = []
  if (props.entry.round > FIRST_ROUND) {
    parts.push($t('shared.agentActivity.round', { round: props.entry.round }))
  }
  if (state.value === 'working' && updatedAgo.value) {
    parts.push(updatedAgo.value)
  }
  // Duration only. Pages-viewed and code-run counts are process detail, not something the
  // reader of a settled row is looking for.
  const duration = props.entry.displayDigest?.receipt.durationSeconds
  if (state.value === 'done' && duration != null) {
    parts.push($t('shared.agentActivity.finishedIn', { duration: spanWord(duration) }))
  }
  return parts.join(' · ')
})

// Trimmed: an unnamed block leaves the role slot empty, and a label ending in a dangling space
// is what a screen reader would read out.
const label = computed(() =>
  (props.fileName
    ? $t('shared.agentActivity.openFeedFor', {
        role: roleLabel.value,
        file: props.fileName,
      })
    : $t('shared.agentActivity.openFeed', { role: roleLabel.value })
  ).trim(),
)
</script>

<style scoped>
/* One agent as a presence: the latest line first at ink weight, the
   specialism as the signature, and every ambient treatment — warmed ground,
   dot, ping — a function of --fresh, real recency decaying to stillness. */
.presence {
  position: relative;
  isolation: isolate;
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
  /* Agents sit side by side; unequal heights read as misalignment rather
     than as one of them having more to say. */
  height: 100%;
  padding: 12px 14px;
  background: color-mix(in srgb, var(--bg-1) 92%, transparent);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  overflow: hidden;
  cursor: pointer;
  font: inherit;
  text-align: left;
  transition:
    border-color 160ms ease,
    box-shadow 160ms ease,
    transform 160ms ease;
}

/* Every working agent sits on the same quiet ground; recency lives in the
   dot's amplitude and the updated-ago text, not the shading. */
.presence.is-working::before {
  content: '';
  position: absolute;
  inset: 0;
  background: var(--presence);
  opacity: 0.04;
  pointer-events: none;
  z-index: -1;
}

.presence:hover {
  border-color: color-mix(in srgb, var(--presence) 45%, var(--stroke));
  box-shadow: var(--shadow-hover);
  transform: translateY(-1px);
}

.presence-caret {
  position: absolute;
  top: 10px;
  right: 10px;
  color: var(--text-muted);
  font-size: 0.65rem;
  transition: color 160ms ease;
}

.presence:hover .presence-caret,
.presence:focus-visible .presence-caret {
  color: var(--presence);
}

.presence:focus-visible {
  outline: 2px solid var(--presence);
  outline-offset: 2px;
}

.presence.is-working {
  border-color: color-mix(in srgb, var(--presence) 30%, var(--stroke));
}

.presence.is-hero {
  border-color: var(--accent-warning-border);
}

.presence.is-done {
  background: color-mix(in srgb, var(--bg-1) 72%, transparent);
  box-shadow: none;
}

.presence.is-failed {
  background: transparent;
  border-style: dashed;
  box-shadow: none;
}

.presence-voice {
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
  color: var(--text-primary);
  font-size: 0.9rem;
  line-height: 1.45;
  text-wrap: pretty;
  padding: 2px 6px;
  margin: -2px -6px;
  border-radius: 6px;
  animation:
    note-in 360ms ease,
    note-land 2400ms ease-out;
}

.presence.is-done .presence-voice,
.presence.is-failed .presence-voice {
  color: var(--text-secondary);
  padding-right: 22px;
}

.voice-tick {
  color: var(--accent-success);
  font-weight: 700;
  margin-right: 4px;
}

.voice-quote {
  color: var(--text-muted);
}

/* The status mark keeps its own column so it stays put at the head of the
   first line. As a plain flex sibling a two-line block name pushed it onto a
   line of its own, so side-by-side agents wore their marks at different
   heights depending on how long their names were. */
.presence-byline {
  margin-top: auto;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: start;
  column-gap: 7px;
  min-width: 0;
  color: var(--text-muted);
  font-size: 0.7rem;
  line-height: 1.3;
}

/* Two lines of name plus one of tail, always — the tail is absent on an
   agent with nothing to report yet, and without the reservation such a
   card sits shorter than a neighbour that has one. The byline is pinned to
   the foot of the card, so any height it borrows from its content moves the
   status mark. Fixing the height here is what keeps a row of marks level
   through every combination of name length, wording, and state. */
.byline-text {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 3px 7px;
  min-width: 0;
  /* two name lines + the 3px row gap + one tail line */
  min-height: calc(3.9em + 3px);
}

/* Always its own line. Left to share a line with the end of the name it
   would sit beside a short one and drop below a long one, and since the
   byline is pinned to the foot of the card that shifted the status mark up
   and down as the wording changed — a row whose marks lined up while the
   agents worked and broke apart as they finished. */
.presence-tail {
  flex: 0 0 100%;
}

/* Centres the mark on the first line rather than its top edge:
   (line-height 1.3 × font-size 0.7rem ≈ 14.6px, less the 8px dot) ÷ 2. */
.byline-dot {
  margin-top: 3px;
  color: var(--presence);
}

.byline-glyph {
  margin-top: 1px;
  font-size: 0.75rem;
}

/* The block name is caller data and can be long ("Balance Sheet, Equity
   Movements & Regulatory Capital"). It used to refuse to shrink, so it ran
   under the card's overflow:hidden and was sliced mid-word at the edge. It
   wraps instead — the byline is already a wrapping flex row. */
.presence-role {
  min-width: 0;
  overflow-wrap: anywhere;
  /* Exactly two lines, whether or not the name fills them. The byline is
     pinned to the foot of the card, so a name that wrapped to two lines made
     its byline taller and started it higher than a neighbour's — which is
     what put side-by-side status marks at different heights. Reserving the
     second line costs a little space under short names and makes the row
     line up whatever the names are. A longer name ellipsises rather than
     taking a third line and breaking it again. */
  min-height: 2.6em;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  font-weight: 650;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-secondary);
}

.presence.is-failed .presence-role {
  color: var(--text-muted);
}

/* The tail carries recency, receipts and the note count — every part earns
   the click, so it wraps rather than truncates. */
.presence-tail {
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

/* Motion only ever accompanies a real event: a new line re-mounts the keyed
   voice span and plays these once. Nothing loops on a timer. */
@keyframes note-in {
  from {
    opacity: 0;
    transform: translateY(4px);
  }

  to {
    transform: translateY(0);
  }
}

@keyframes note-land {
  0% {
    background-color: color-mix(in srgb, var(--presence) 14%, transparent);
  }

  100% {
    background-color: transparent;
  }
}

@media (prefers-reduced-motion: reduce) {
  .presence-voice {
    animation: none;
  }

  .presence,
  .presence-caret {
    transition: none;
  }

  .presence:hover {
    transform: none;
  }
}
</style>
