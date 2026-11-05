import { describe, expect, it } from 'vitest'
import {
  DEFAULT_QUERY_STATE,
  buildDocumentsQuery,
  parseQueryState,
  serializeQueryState,
  type DocumentsQueryState,
} from './documentsQuery'

const withDefaults = (
  partial: Partial<DocumentsQueryState>,
): DocumentsQueryState => ({ ...DEFAULT_QUERY_STATE, ...partial })

describe('buildDocumentsQuery', () => {
  it('produces only $skip/$top/$count/$orderby at defaults', () => {
    expect(buildDocumentsQuery(DEFAULT_QUERY_STATE)).toEqual({
      $skip: 0,
      $top: 25,
      $count: true,
      $orderby: 'createdAt desc',
    })
  })

  it('searches by name whatever the capitalisation', () => {
    for (const search of ['invoice', 'Invoice', 'INVOICE']) {
      expect(buildDocumentsQuery(withDefaults({ search })).$filter).toBe("(contains(tolower(name), 'invoice'))")
    }
  })

  it("escapes single quotes in search (Amy's -> amy''s)", () => {
    const q = buildDocumentsQuery(withDefaults({ search: "Amy's" }))
    expect(q.$filter).toBe("(contains(tolower(name), 'amy''s'))")
  })

  it('composes filters in fixed order with " and "', () => {
    const q = buildDocumentsQuery(
      withDefaults({
        search: 'Invoice',
        mediaType: 'pdf',
        categoryId: 'c1',
        userId: 'u1',
      }),
    )
    expect(q.$filter).toBe(
      "(contains(tolower(name), 'invoice')) and mediaType eq 'ApplicationPdf' and documentCategoryId eq 'c1' and userId eq 'u1'",
    )
  })

  it('passes jobId through as a sibling param, not inside $filter', () => {
    const q = buildDocumentsQuery(withDefaults({ jobId: 'job-1', mediaType: 'pdf' }))
    expect(q.jobId).toBe('job-1')
    expect(q.$filter).toBe("mediaType eq 'ApplicationPdf'")
  })

  it('uses group-based filter fragments for media type', () => {
    const imageQ = buildDocumentsQuery(withDefaults({ mediaType: 'image' }))
    expect(imageQ.$filter).toBe("startswith(mediaType, 'Image')")

    const excelQ = buildDocumentsQuery(withDefaults({ mediaType: 'excel' }))
    expect(excelQ.$filter).toBe(
      "(contains(mediaType, 'Excel') or contains(mediaType, 'Spreadsheet'))",
    )
  })

  it('drops an unknown media type group rather than sending garbage', () => {
    const q = buildDocumentsQuery(withDefaults({ mediaType: 'nope' }))
    expect('$filter' in q).toBe(false)
  })

  it('computes $skip from page and pageSize', () => {
    const q = buildDocumentsQuery(withDefaults({ page: 3, pageSize: 25 }))
    expect(q.$skip).toBe(75)
    expect(q.$top).toBe(25)
  })

  it('passes orderBy through to $orderby', () => {
    const q = buildDocumentsQuery(withDefaults({ orderBy: 'name asc' }))
    expect(q.$orderby).toBe('name asc')
  })

  it('omits $filter when every filter field is empty', () => {
    const q = buildDocumentsQuery(DEFAULT_QUERY_STATE)
    expect('$filter' in q).toBe(false)
  })
})

describe('parseQueryState', () => {
  it('returns defaults for an empty query', () => {
    expect(parseQueryState({})).toEqual(DEFAULT_QUERY_STATE)
  })

  it('reads known keys and coerces numerics', () => {
    expect(
      parseQueryState({
        page: '2',
        pageSize: '50',
        orderby: 'name asc',
        search: 'foo',
        mediaType: 'pdf',
        category: 'c1',
        user: 'u1',
        jobId: 'j1',
      }),
    ).toEqual({
      page: 2,
      pageSize: 50,
      orderBy: 'name asc',
      search: 'foo',
      mediaType: 'pdf',
      categoryId: 'c1',
      userId: 'u1',
      jobId: 'j1',
    })
  })

  it('normalizes mediaType casing to canonical lowercase group ids', () => {
    expect(parseQueryState({ mediaType: 'Pdf' }).mediaType).toBe('pdf')
    expect(parseQueryState({ mediaType: 'IMAGE' }).mediaType).toBe('image')
    expect(parseQueryState({ mediaType: 'eXcEl' }).mediaType).toBe('excel')
  })

  it('drops unknown mediaType values rather than passing them through', () => {
    expect(parseQueryState({ mediaType: 'nope' }).mediaType).toBe('')
    expect(parseQueryState({ mediaType: 'ApplicationPdf' }).mediaType).toBe('')
  })

  it('clamps page to >= 0 and pageSize to [10, 100]', () => {
    expect(parseQueryState({ page: '-1' }).page).toBe(0)
    expect(parseQueryState({ pageSize: '9999' }).pageSize).toBe(100)
    expect(parseQueryState({ pageSize: '3' }).pageSize).toBe(10)
  })

  it('falls back to defaults for non-numeric page/pageSize', () => {
    expect(parseQueryState({ page: 'abc', pageSize: 'x' })).toMatchObject({
      page: 0,
      pageSize: 25,
    })
  })

  it('ignores array-valued query entries (takes the first or falls back)', () => {
    expect(parseQueryState({ search: ['a', 'b'] }).search).toBe('a')
  })
})

describe('serializeQueryState', () => {
  it('omits every entry equal to its default', () => {
    expect(serializeQueryState(DEFAULT_QUERY_STATE)).toEqual({})
  })

  it('emits non-default entries as strings', () => {
    const out = serializeQueryState(
      withDefaults({ page: 2, pageSize: 50, jobId: 'j1', mediaType: 'pdf' }),
    )
    expect(out).toEqual({
      page: '2',
      pageSize: '50',
      mediaType: 'pdf',
      jobId: 'j1',
    })
  })

  it('round-trips parseQueryState(serializeQueryState(state)) === state', () => {
    const cases: DocumentsQueryState[] = [
      DEFAULT_QUERY_STATE,
      withDefaults({ page: 4 }),
      withDefaults({
        search: "Amy's",
        mediaType: 'image',
        categoryId: 'c1',
        userId: 'u1',
        orderBy: 'name desc',
        pageSize: 50,
        jobId: 'j1',
      }),
    ]
    for (const s of cases) {
      expect(parseQueryState(serializeQueryState(s))).toEqual(s)
    }
  })
})
