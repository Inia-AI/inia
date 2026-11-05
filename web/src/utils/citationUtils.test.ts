import { describe, it, expect } from 'vitest'
import {
  aggregateSources,
  citedSourcesOf,
  documentSourceLabel,
  drawnFromSources,
  isUnverifiedDocumentSource,
  isWebUrl,
} from './citationUtils'
import type {
  BlockCitationSource,
  DocumentCitationSource,
  UrlCitationSource,
} from '@/api/workflows/schema'

const verifiedSource = (overrides: Partial<DocumentCitationSource> = {}): DocumentCitationSource => ({
  type: 'Document',
  citedText: 'Total assets 4,027,553,656',
  pageId: 'page-1',
  pageNumber: 2,
  filePageNumber: 2,
  documentName: 'ar.pdf',
  ...overrides
})

const unverifiedSource = (citedText: string, documentName = 'ar.pdf'): DocumentCitationSource => ({
  type: 'Document',
  citedText,
  pageId: '',
  pageNumber: 0,
  filePageNumber: 0,
  documentName,
  unverified: true
})

describe('aggregateSources', () => {
  it('merges document sources that resolved to the same page', () => {
    const aggregated = aggregateSources([
      verifiedSource({ citedText: 'first quote' }),
      verifiedSource({ citedText: 'second quote' })
    ])
    expect(aggregated).toHaveLength(1)
    expect(aggregated[0]?.citedTexts).toEqual(['first quote', 'second quote'])
  })

  it('keeps unverified sources with distinct quotes separate', () => {
    const aggregated = aggregateSources([
      unverifiedSource('Invented impairment 19,800,000'),
      unverifiedSource('Another missing line 1,000')
    ])
    expect(aggregated).toHaveLength(2)
  })

  it('keeps the same quote separate across different documents', () => {
    const aggregated = aggregateSources([
      unverifiedSource('Same quote 1,000', 'first.pdf'),
      unverifiedSource('Same quote 1,000', 'second.pdf')
    ])
    expect(aggregated).toHaveLength(2)
  })

  it('keys an unverified source by the flag, not by an empty pageId', () => {
    const bestGuess: DocumentCitationSource = {
      type: 'Document',
      citedText: 'quote',
      pageId: 'page-1',
      pageNumber: 2,
      documentName: 'ar.pdf',
      unverified: true
    }
    const aggregated = aggregateSources([bestGuess, verifiedSource()])
    expect(aggregated).toHaveLength(2)
  })

  it('still merges unverified sources carrying the same quote', () => {
    const aggregated = aggregateSources([
      unverifiedSource('Invented impairment 19,800,000'),
      unverifiedSource('Invented impairment 19,800,000')
    ])
    expect(aggregated).toHaveLength(1)
  })
})

describe('documentSourceLabel', () => {
  it('names the document and the page for a resolved source', () => {
    expect(documentSourceLabel(verifiedSource(), 'Document')).toBe('ar.pdf - page 2')
  })

  it('names only the document for an unverified source', () => {
    expect(documentSourceLabel(unverifiedSource('quote'), 'Document')).toBe('ar.pdf')
  })

  it('falls back to the given name when the source has none', () => {
    expect(documentSourceLabel(verifiedSource({ documentName: undefined }), 'Document')).toBe(
      'Document - page 2'
    )
  })
})

describe('isUnverifiedDocumentSource', () => {
  it('is true only for a document source flagged unverified', () => {
    expect(isUnverifiedDocumentSource(unverifiedSource('quote'))).toBe(true)
    expect(isUnverifiedDocumentSource(verifiedSource())).toBe(false)
    expect(isUnverifiedDocumentSource({ type: 'Url', citedText: '', url: 'https://x' })).toBe(false)
  })
})

const url = (href: string, citedText = 'excerpt'): UrlCitationSource => ({ type: 'Url', url: href, citedText })

const blockSource = (overrides: Partial<BlockCitationSource> = {}): BlockCitationSource => ({
  type: 'Block',
  blockId: 'block-synthesis',
  blockTitle: 'Key Principals',
  citedText: 'a paraphrase',
  upstreamSources: [],
  ...overrides
})

describe('drawnFromSources', () => {
  it('puts the sources that state the fact first, then the rest in chain order', () => {
    const source = blockSource({
      matchingSources: [url('https://example.org/people/alex-rivera', 'start date: 2011-05-01')]
    })

    const drawn = drawnFromSources(source, [
      verifiedSource(),
      url('https://example.org/team'),
      url('https://example.org/people/alex-rivera')
    ])

    expect(drawn.map(d => d.matchesFact)).toEqual([true, false, false])
    expect((drawn[0]?.source as UrlCitationSource).url).toBe('https://example.org/people/alex-rivera')
    expect(drawn[0]?.citedTexts).toEqual(['start date: 2011-05-01'])
    expect(drawn[1]?.source.type).toBe('Document')
    expect((drawn[2]?.source as UrlCitationSource).url).toBe('https://example.org/team')
  })

  it('lists one entry per page and per link', () => {
    const drawn = drawnFromSources(blockSource(), [
      verifiedSource({ citedText: 'first' }),
      verifiedSource({ citedText: 'second' }),
      url('https://a'),
      url('https://a')
    ])

    expect(drawn).toHaveLength(2)
  })

  it('is empty for a block citation with no list', () => {
    expect(drawnFromSources(blockSource(), undefined)).toEqual([])
  })
})

describe('citedSourcesOf', () => {
  const lists = [
    { blockId: 'block-synthesis', sources: [verifiedSource()] },
    { blockId: 'block-research', sources: [url('https://example.org/team')] }
  ]

  it('finds the list stored for the block the citation points to', () => {
    expect(citedSourcesOf(blockSource({ blockId: 'block-research' }), lists)).toEqual(lists[1]?.sources)
  })

  it('finds nothing for a paragraph built before the lists existed', () => {
    expect(citedSourcesOf(blockSource(), [])).toBeUndefined()
  })
})

describe('isWebUrl', () => {
  it('accepts a web address in any letter case', () => {
    expect(isWebUrl('https://example.org/northwind')).toBe(true)
    expect(isWebUrl('HTTP://example.org/northwind')).toBe(true)
  })

  it('refuses any other scheme', () => {
    expect(isWebUrl('javascript:alert(1)')).toBe(false)
    expect(isWebUrl('ftp://example.org/file')).toBe(false)
  })
})
