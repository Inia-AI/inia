import type { BlockCitationSource, BlockCitedSources } from '@/api/workflows/schema'

export const SOURCE_CHAIN_EVENT = 'source-chain:open'

export interface SourceChainEventDetail {
  blockSource: BlockCitationSource
  /** The lists of the paragraph the trace started from, so every step of the chain can show its own. */
  citedSourcesByBlock: BlockCitedSources[]
  opener?: HTMLElement
  fallback?: HTMLElement
}
