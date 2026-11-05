import { computed, onScopeDispose, ref, unref, watch, type MaybeRef } from 'vue'
import { WorkflowState } from '@/enums'
import { formatDuration, formatDurationMs, parseServerDate } from '@/lib/utils'

interface DurationSource {
  duration?: string | null
  startedAt?: string | null
  workflowState?: WorkflowState
}

const IN_FLIGHT_STATES: ReadonlySet<WorkflowState> = new Set([
  WorkflowState.Queued,
  WorkflowState.Running,
  WorkflowState.ProcessingDocuments,
])

/**
 * Reactive, formatted duration for a workflow execution. While the run is
 * in-flight (Queued/Running/ProcessingDocuments) and `startedAt` is set, ticks
 * once per second and shows elapsed time. Once the run reaches a terminal
 * state — or whenever the source has no `startedAt` yet — falls back to the
 * server-side `duration` field. Returns null when nothing useful can be shown
 * so callers can decide whether to render the chip at all.
 */
export const useLiveDuration = (source: MaybeRef<DurationSource | undefined>) => {
  const now = ref(Date.now())
  let timer: ReturnType<typeof setInterval> | null = null

  const isLiveTicking = computed(() => {
    const data = unref(source)
    return (
      !!data?.workflowState
      && IN_FLIGHT_STATES.has(data.workflowState)
      && !!data.startedAt
    )
  })

  watch(
    isLiveTicking,
    ticking => {
      if (ticking && timer === null) {
        now.value = Date.now()
        timer = setInterval(() => {
          now.value = Date.now()
        }, 1_000)
      } else if (!ticking && timer !== null) {
        clearInterval(timer)
        timer = null
      }
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  })

  return computed<string | null>(() => {
    const data = unref(source)
    if (!data) return null

    if (isLiveTicking.value && data.startedAt) {
      const elapsed = now.value - parseServerDate(data.startedAt).getTime()
      return formatDurationMs(elapsed)
    }

    const formatted = formatDuration(data.duration)
    return formatted === '-' ? null : formatted
  })
}
