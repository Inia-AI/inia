export const PEER_CANDIDATE_PAGE_SIZE = 200

export interface PeerCandidate {
  id: string
  name: string
  nameVariants: string
  reportedName?: string | null
  isEligible: boolean
  availability: string
  currency?: string | null
  latestPeriodEnd?: string | null
}

export interface PeerCandidateQueryParams {
  workflowTypeId?: string
  search?: string
  top?: number
}
