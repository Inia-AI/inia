export type RunProgressLayout = 'both' | 'agents' | 'sections' | 'state' | 'none'

export interface RunProgressFacts {
  agentCount: number
  hasSectionProgress: boolean
  hasStateLine: boolean
}

export const runProgressLayoutOf = ({
  agentCount,
  hasSectionProgress,
  hasStateLine,
}: RunProgressFacts): RunProgressLayout => {
  const hasAgents = agentCount > 0
  if (hasAgents && hasSectionProgress) {
    return 'both'
  }
  if (hasAgents) {
    return 'agents'
  }
  if (hasSectionProgress) {
    return 'sections'
  }
  return hasStateLine ? 'state' : 'none'
}

export const showsAgents = (layout: RunProgressLayout): boolean =>
  layout === 'both' || layout === 'agents'

export const showsSections = (layout: RunProgressLayout): boolean =>
  layout === 'both' || layout === 'sections'
