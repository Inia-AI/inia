export type AgentActivityOutcome =
  | 'Unknown'
  | 'Running'
  | 'Succeeded'
  | 'Failed'

export type AgentActivityBeatKind = 'Unknown' | 'Normal' | 'Hero' | 'Check'

export interface AgentActivityPhase {
  label: string
  gapLabel: string
}

export interface AgentActivityBeat {
  atSeconds: number
  phaseIndex: number
  kind: AgentActivityBeatKind
  label: string
  detail?: string | null
}

export interface AgentActivityReceipt {
  durationSeconds?: number | null
  documentNames: string[]
  skillNames: string[]
  pagesViewed: number
  codeRuns: number
  checksReported: number
}

export interface AgentActivityDigest {
  schemaVersion: number
  outcome: AgentActivityOutcome
  startedAtUtc?: string | null
  phases: AgentActivityPhase[]
  beats: AgentActivityBeat[]
  receipt: AgentActivityReceipt
}

export const WORKER_ROLE = 'w'
export const VERIFIER_ROLE = 'v'

export interface AgentBlockActivity {
  blockId: string
  blockName: string
  round?: number | null
  role?: string | null
  settled: boolean
  activity?: AgentActivityDigest | null
}
