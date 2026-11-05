import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { WorkflowState } from '@/enums'
import {
  VERIFIER_ROLE,
  type AgentActivityDigest,
  type AgentBlockActivity,
} from '@/api/agentActivity/schema'
import type { WorkflowActivity } from '@/api/workflowReports/schema'
import { activityViewOf, type WorkflowActivityView } from '@/lib/workflowActivity'

const STALE_ENTRY_MS = 15 * 60 * 1000
export const FIRST_ROUND = 1

export type AgentLegRole = 'worker' | 'verifier'

export interface AgentActivityReport {
  id: string
  title?: string
  counterpartyName?: string
  workflowState?: WorkflowState
  agentActivity?: AgentBlockActivity[] | null
  workflowActivity?: WorkflowActivity | null
}

export type ClassicActivityEntry = WorkflowActivityView & {
  lastSeenAt: number
  watchedFromStart: boolean
}

export interface AgentRailEntry {
  key: string
  executionId: string
  executionTitle: string
  counterpartyName: string
  blockId: string
  blockName: string
  round: number
  role: AgentLegRole
  settled: boolean
  digest: AgentActivityDigest | null
  finalDigest: AgentActivityDigest | null
  displayDigest: AgentActivityDigest | null
  lastSeenAt: number
  lastBeatAt: number
}

// Not the run id: a machinery retry inside one round starts a second run under a new id, so
// keying on the run would split one round into two rows.
const legKeyOf = (executionId: string, block: AgentBlockActivity): string =>
  `${executionId}:${block.blockId}:r${block.round ?? FIRST_ROUND}`

const blockKeyOf = (entry: AgentRailEntry): string =>
  `${entry.executionId}:${entry.blockId}`

const IN_FLIGHT_STATES: readonly WorkflowState[] = [
  WorkflowState.Running,
  WorkflowState.ProcessingDocuments,
  WorkflowState.Queued,
]

const hasEnded = (state: WorkflowState | undefined): boolean =>
  state !== undefined && !IN_FLIGHT_STATES.includes(state)

export default defineStore('agentActivity', () => {
  const entries = ref<Map<string, AgentRailEntry>>(new Map())
  const classicEntries = ref<Map<string, ClassicActivityEntry>>(new Map())
  const seenBeforeActivity = ref<Map<string, number>>(new Map())
  const panelKey = ref<string | null>(null)

  const railEntries = computed(() => {
    const currentLegs = new Map<string, AgentRailEntry>()
    for (const entry of entries.value.values()) {
      const blockKey = blockKeyOf(entry)
      const held = currentLegs.get(blockKey)
      if (held === undefined || entry.round > held.round) {
        currentLegs.set(blockKey, entry)
      }
    }
    return [...currentLegs.values()]
  })

  const panelEntry = computed(
    () => (panelKey.value && entries.value.get(panelKey.value)) || null,
  )

  const withDisplayDigest = (entry: AgentRailEntry): AgentRailEntry => ({
    ...entry,
    displayDigest: entry.finalDigest ?? entry.digest,
  })

  const anchoredLastBeatAt = (
    digest: AgentActivityDigest | null,
  ): number | null => {
    const lastBeat = digest?.beats.at(-1)
    if (!digest?.startedAtUtc || !lastBeat) {
      return null
    }
    const anchor = Date.parse(digest.startedAtUtc)
    return Number.isFinite(anchor) ? anchor + lastBeat.atSeconds * 1000 : null
  }

  const ingestClassicActivity = (report: AgentActivityReport) => {
    const view =
      report.workflowState === WorkflowState.Running
        ? activityViewOf(report.workflowActivity)
        : null
    if (view === null) {
      classicEntries.value.delete(report.id)
      seenBeforeActivity.value.set(report.id, Date.now())
      return
    }
    const existing = classicEntries.value.get(report.id)
    classicEntries.value.set(report.id, {
      ...view,
      lastSeenAt: Date.now(),
      watchedFromStart: existing?.watchedFromStart ?? seenBeforeActivity.value.has(report.id),
    })
  }

  const classicActivityOf = (executionId: string): ClassicActivityEntry | null =>
    classicEntries.value.get(executionId) ?? null

  // A cancelled run reports no members at all, so nothing would ever settle the rows it left.
  const releaseUnreportedLegs = (executionId: string, reportedKeys: Set<string>) => {
    for (const [key, entry] of entries.value) {
      if (entry.executionId !== executionId || reportedKeys.has(key)) continue
      if (key === panelKey.value) {
        entries.value.set(key, { ...entry, settled: true })
      } else {
        entries.value.delete(key)
      }
    }
  }

  const ingestReport = (report: AgentActivityReport) => {
    ingestClassicActivity(report)
    const reportedKeys = new Set<string>()
    for (const block of report.agentActivity ?? []) {
      const key = legKeyOf(report.id, block)
      reportedKeys.add(key)
      const existing = entries.value.get(key)
      const digest = block.activity ?? existing?.digest ?? null
      const beatCount = digest?.beats.length ?? 0
      const knownBeatCount = existing?.digest?.beats.length ?? 0
      const lastBeatAt =
        anchoredLastBeatAt(digest) ??
        (existing === undefined || beatCount > knownBeatCount
          ? Date.now()
          : existing.lastBeatAt)
      entries.value.set(
        key,
        withDisplayDigest({
          key,
          executionId: report.id,
          executionTitle: report.title ?? existing?.executionTitle ?? '',
          counterpartyName:
            report.counterpartyName ?? existing?.counterpartyName ?? '',
          blockId: block.blockId,
          blockName: block.blockName,
          round: block.round ?? FIRST_ROUND,
          role: block.role === VERIFIER_ROLE ? 'verifier' : 'worker',
          // Sticky, because an out-of-order poll must not flap a finished run back to running.
          // Known gap: a machinery retry's second pass shares this leg and reads settled while it
          // runs, which needs the pass on the wire to fix rather than a weaker latch.
          settled: block.settled || (existing?.settled ?? false),
          digest,
          finalDigest: existing?.finalDigest ?? null,
          displayDigest: null,
          lastSeenAt: Date.now(),
          lastBeatAt,
        }),
      )
    }
    if (hasEnded(report.workflowState)) {
      releaseUnreportedLegs(report.id, reportedKeys)
    }
  }

  const setFinalDigest = (key: string, digest: AgentActivityDigest) => {
    const existing = entries.value.get(key)
    if (!existing) return
    entries.value.set(
      key,
      withDisplayDigest({ ...existing, finalDigest: digest, settled: true }),
    )
  }

  const openPanel = (key: string) => {
    if (entries.value.has(key)) {
      panelKey.value = key
    }
  }

  const closePanel = () => {
    panelKey.value = null
  }

  // Every leg, or the row hands itself back to the round the reader just dismissed.
  const dismiss = (key: string) => {
    const dismissed = entries.value.get(key)
    if (dismissed === undefined) {
      return
    }
    for (const [legKey, entry] of entries.value) {
      if (entry.executionId === dismissed.executionId && entry.blockId === dismissed.blockId) {
        entries.value.delete(legKey)
      }
    }
    if (panelKey.value !== null && !entries.value.has(panelKey.value)) {
      panelKey.value = null
    }
  }

  // By the newest leg, and all legs together. Per leg would hand the row back to a round the
  // member has left; by the oldest leg would drop a member still running, because the feed stops
  // refreshing an earlier round as soon as it lets it go.
  const prune = (now: number = Date.now()) => {
    const newestSeenByBlock = new Map<string, number>()
    for (const entry of entries.value.values()) {
      const blockKey = blockKeyOf(entry)
      newestSeenByBlock.set(
        blockKey,
        Math.max(newestSeenByBlock.get(blockKey) ?? 0, entry.lastSeenAt),
      )
    }
    for (const [key, entry] of entries.value) {
      if (key === panelKey.value) continue
      const newestSeenAt = newestSeenByBlock.get(blockKeyOf(entry)) ?? entry.lastSeenAt
      if (now - newestSeenAt >= STALE_ENTRY_MS) {
        entries.value.delete(key)
      }
    }
    for (const [executionId, entry] of classicEntries.value) {
      if (now - entry.lastSeenAt >= STALE_ENTRY_MS) {
        classicEntries.value.delete(executionId)
      }
    }
    for (const [executionId, seenAt] of seenBeforeActivity.value) {
      if (now - seenAt >= STALE_ENTRY_MS) {
        seenBeforeActivity.value.delete(executionId)
      }
    }
  }

  return {
    railEntries,
    panelEntry,
    ingestReport,
    classicActivityOf,
    setFinalDigest,
    openPanel,
    closePanel,
    dismiss,
    prune,
  }
})
