import type { PeerCandidate } from '@/api/peerCandidates/schema'
import type { PeerOptionType } from '@/types'
import { formatMonth } from '@/lib/utils'

type TranslateFn = (key: string, params?: Record<string, unknown>) => string

const REASON_KEY_PREFIX = 'shared.components.peerCompanySelect'

const REASON_KEY_BY_AVAILABILITY: Record<string, string> = {
  NotAnalysed: `${REASON_KEY_PREFIX}.notAnalysed`,
  Unreadable: `${REASON_KEY_PREFIX}.unreadable`,
  NoUsablePeriods: `${REASON_KEY_PREFIX}.noUsablePeriods`,
}

const UNRECOGNISED_REASON_KEY = `${REASON_KEY_PREFIX}.unavailable`

const unavailableReasonOf = (
  candidate: PeerCandidate,
  t: TranslateFn,
): string | undefined => {
  if (candidate.isEligible) return undefined

  return t(
    REASON_KEY_BY_AVAILABILITY[candidate.availability] ??
      UNRECOGNISED_REASON_KEY,
  )
}

const figuresOf = (
  candidate: PeerCandidate,
  t: TranslateFn,
): string | undefined => {
  if (!candidate.isEligible || !candidate.latestPeriodEnd) return undefined

  const date = formatMonth(candidate.latestPeriodEnd)

  return candidate.currency
    ? t(`${REASON_KEY_PREFIX}.figuresToInCurrency`, {
        date,
        currency: candidate.currency,
      })
    : t(`${REASON_KEY_PREFIX}.figuresTo`, { date })
}

const reportedAsOf = (
  candidate: PeerCandidate,
  t: TranslateFn,
): string | undefined =>
  candidate.reportedName && candidate.reportedName !== candidate.name
    ? t(`${REASON_KEY_PREFIX}.reportedAs`, { name: candidate.reportedName })
    : undefined

const searchTextOf = (candidate: PeerCandidate): string =>
  [candidate.name, candidate.nameVariants, candidate.reportedName]
    .filter(Boolean)
    .join(' ')

export const peerCandidateToOption = (
  candidate: PeerCandidate,
  t: TranslateFn,
): PeerOptionType => {
  const unavailableReason = unavailableReasonOf(candidate, t)
  const figures = figuresOf(candidate, t)
  const reportedAs = reportedAsOf(candidate, t)

  return {
    label: candidate.name,
    value: candidate.id,
    searchText: searchTextOf(candidate),
    ...(unavailableReason ? { unavailableReason } : {}),
    ...(figures ? { figures } : {}),
    ...(reportedAs ? { reportedAs } : {}),
  }
}

export const peerCandidateOptions = (
  candidates: PeerCandidate[] | undefined,
  { excludeId, t }: { excludeId?: string; t: TranslateFn },
): PeerOptionType[] =>
  (candidates ?? [])
    .filter(candidate => candidate.id !== excludeId)
    .map(candidate => peerCandidateToOption(candidate, t))

export const withRememberedPeerOptions = (
  current: PeerOptionType[],
  remembered: PeerOptionType[],
  selectedIds: string[],
  excludeId?: string,
): PeerOptionType[] => {
  const present = new Set(current.map(option => option.value))

  const missingSelections = remembered.filter(
    option =>
      option.value !== excludeId &&
      selectedIds.includes(option.value) &&
      !present.has(option.value),
  )

  return [...current, ...missingSelections]
}
