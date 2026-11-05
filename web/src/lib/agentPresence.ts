import type { AgentActivityBeat } from '@/api/agentActivity/schema'

export const FRESHNESS_WINDOW_MS = 5 * 60 * 1000

export const freshnessOf = (
  lastBeatAt: number | null | undefined,
  now: number,
): number => {
  if (lastBeatAt == null) {
    return 0
  }
  const ageMs = Math.max(0, now - lastBeatAt)
  return Math.max(0, Math.min(1, 1 - ageMs / FRESHNESS_WINDOW_MS))
}

export const beatKeysOf = (beats: AgentActivityBeat[]): string[] => {
  const occurrences = new Map<string, number>()
  return beats.map(beat => {
    const identity = `${beat.atSeconds}:${beat.kind}:${beat.label}`
    const occurrence = occurrences.get(identity) ?? 0
    occurrences.set(identity, occurrence + 1)
    return occurrence === 0 ? identity : `${identity}:${occurrence}`
  })
}

export type CollapsedBeat = {
  beat: AgentActivityBeat
  key: string
  repeat: number
}

/**
 * A run that writes six working files in a row earns six beats carrying the
 * same static per-kind label, which reads as the feed stuttering rather than
 * as work. Consecutive beats saying exactly the same thing collapse into the
 * first one, which then carries how many times it happened.
 *
 * Only adjacent beats collapse: a repeat interrupted by different work is a
 * genuinely separate stretch and stays on its own line. The surviving beat is
 * the earliest of the run, so the time still marks when the stretch began.
 */
export const collapseRepeatedBeats = (
  beats: AgentActivityBeat[],
): CollapsedBeat[] => {
  const keys = beatKeysOf(beats)
  const collapsed: CollapsedBeat[] = []
  for (const [index, beat] of beats.entries()) {
    const previous = collapsed[collapsed.length - 1]
    const sameAsPrevious =
      previous !== undefined &&
      previous.beat.label === beat.label &&
      previous.beat.kind === beat.kind &&
      (previous.beat.detail ?? null) === (beat.detail ?? null)
    if (sameAsPrevious) {
      previous.repeat += 1
      continue
    }
    collapsed.push({ beat, key: keys[index] as string, repeat: 1 })
  }
  return collapsed
}
