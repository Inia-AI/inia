import type {
  BlockCitationSource,
  BlockCitedSources,
  CitationSource,
  DocumentCitationSource,
  UrlCitationSource,
} from '@/api/workflows/schema'
import { isDocumentSource, isUrlSource, isBlockSource } from '@/api/workflows/schema'

/** The popup and the chain panel show this many pages and links before "Show all". */
export const DRAWN_FROM_VISIBLE_COUNT = 5

/**
 * Represents a source with aggregated cited texts (for sources that appear multiple times)
 */
export interface AggregatedSource {
  source: CitationSource
  citedTexts: string[]
}

/** A page or link behind a block citation, and whether its excerpt states the cited fact. */
export interface DrawnFromSource extends AggregatedSource {
  source: DocumentCitationSource | UrlCitationSource
  matchesFact: boolean
}

/** Whether a cited link is a web address, the only kind a reader may open from a citation. */
export function isWebUrl(url: string): boolean {
  return /^https?:\/\//i.test(url)
}

/**
 * The key two sources share when they point at the same page or link. An unverified document
 * source has no page, so its quote tells two apart.
 */
function pageOrLinkKey(source: DocumentCitationSource | UrlCitationSource): string {
  if (isUrlSource(source)) {
    return source.url
  }
  return isUnverifiedDocumentSource(source)
    ? `unverified:${source.documentName ?? ''}:${source.citedText}`
    : source.pageId
}

/**
 * Aggregates citation sources by deduplicating documents and URLs while collecting their cited texts.
 * Block sources are not deduplicated (each appears separately).
 *
 * @param sources - Array of citation sources to aggregate
 * @returns Aggregated sources with block sources first, then documents, then URLs
 */
export function aggregateSources(sources: CitationSource[]): AggregatedSource[] {
  const blockSources: AggregatedSource[] = []
  const documentMap = new Map<string, AggregatedSource>()
  const urlMap = new Map<string, AggregatedSource>()

  for (const source of sources) {
    if (isBlockSource(source)) {
      // Block sources go first and are not aggregated
      blockSources.push({ source, citedTexts: source.citedText ? [source.citedText] : [] })
      continue
    }

    const map = isDocumentSource(source) ? documentMap : urlMap
    const key = pageOrLinkKey(source)
    const existing = map.get(key)
    if (existing) {
      if (source.citedText && !existing.citedTexts.includes(source.citedText)) {
        existing.citedTexts.push(source.citedText)
      }
    } else {
      map.set(key, { source, citedTexts: source.citedText ? [source.citedText] : [] })
    }
  }

  // Return block sources first, then documents, then URLs
  return [
    ...blockSources,
    ...Array.from(documentMap.values()),
    ...Array.from(urlMap.values())
  ]
}

/**
 * The block's list of pages and links, from the paragraph's lists, or undefined for a report
 * built before the lists existed.
 */
export function citedSourcesOf(
  blockSource: BlockCitationSource,
  citedSourcesByBlock: BlockCitedSources[],
): CitationSource[] | undefined {
  return citedSourcesByBlock.find(list => list.blockId === blockSource.blockId)?.sources
}

/**
 * The pages and links behind a block citation, one entry per page or link: first those whose
 * excerpt states the cited fact, then the rest of the block's list in chain order.
 */
export function drawnFromSources(
  blockSource: BlockCitationSource,
  citedSources: CitationSource[] | undefined,
): DrawnFromSource[] {
  const drawn = new Map<string, DrawnFromSource>()

  const add = (sources: CitationSource[] | undefined, matchesFact: boolean) => {
    for (const source of sources ?? []) {
      if (isBlockSource(source)) {
        continue
      }
      const key = `${source.type}:${pageOrLinkKey(source)}`
      if (!drawn.has(key)) {
        drawn.set(key, { source, citedTexts: source.citedText ? [source.citedText] : [], matchesFact })
      }
    }
  }

  add(blockSource.matchingSources, true)
  add(citedSources, false)

  return Array.from(drawn.values())
}

/**
 * Combines multiple cited texts into a single string with separators
 */
export function combineCitedTexts(texts: string[]): string | null {
  if (texts.length === 0) return null
  if (texts.length === 1) return texts[0] ?? null
  return texts.join('\n\n...\n\n')
}

export function isUnverifiedDocumentSource(source: CitationSource): boolean {
  return isDocumentSource(source) && source.unverified === true
}

export function documentSourceLabel(source: DocumentCitationSource, fallbackName: string): string {
  const docName = source.documentName || fallbackName
  return source.unverified ? docName : `${docName} - page ${source.pageNumber}`
}
