import type { LocationQuery } from 'vue-router'
import type { DocumentsQueryParams } from '@/api/documents/schema'
import { odataContainsAny, quoteODataLiteral } from '@/utils/odata'
import { MEDIA_TYPE_GROUPS, mediaTypeFilterByGroup } from './formatMediaType'

export interface DocumentsQueryState {
  page: number
  pageSize: number
  orderBy: string
  search: string
  mediaType: string
  categoryId: string
  userId: string
  jobId: string
}

export const DEFAULT_QUERY_STATE: DocumentsQueryState = {
  page: 0,
  pageSize: 25,
  orderBy: 'createdAt desc',
  search: '',
  mediaType: '',
  categoryId: '',
  userId: '',
  jobId: '',
}

const MIN_PAGE_SIZE = 10
const MAX_PAGE_SIZE = 100

const KNOWN_MEDIA_TYPE_IDS: ReadonlySet<string> = new Set(
  MEDIA_TYPE_GROUPS.map(g => g.id),
)

export function buildDocumentsQuery(state: DocumentsQueryState): DocumentsQueryParams {
  const params: DocumentsQueryParams = {
    $skip: state.page * state.pageSize,
    $top: state.pageSize,
    $count: true,
    $orderby: state.orderBy,
  }

  const filters: string[] = []
  const nameSearch = odataContainsAny(['name'], state.search)
  if (nameSearch) filters.push(nameSearch)
  if (state.mediaType) {
    const fragment = mediaTypeFilterByGroup(state.mediaType)
    if (fragment) filters.push(fragment)
  }
  if (state.categoryId) {
    filters.push(`documentCategoryId eq ${quoteODataLiteral(state.categoryId)}`)
  }
  if (state.userId) {
    filters.push(`userId eq ${quoteODataLiteral(state.userId)}`)
  }
  if (filters.length > 0) {
    params.$filter = filters.join(' and ')
  }

  if (state.jobId) {
    params.jobId = state.jobId
  }

  return params
}

const firstValue = (value: string | string[] | null | undefined): string => {
  if (Array.isArray(value)) return value[0] ?? ''
  return value ?? ''
}

const parseIntegerParam = (raw: string, fallback: number): number => {
  if (raw === '') return fallback
  const n = Number(raw)
  return Number.isFinite(n) && Number.isInteger(n) ? n : fallback
}

const parseMediaTypeParam = (raw: string): string => {
  if (!raw) return ''
  const normalized = raw.toLowerCase()
  return KNOWN_MEDIA_TYPE_IDS.has(normalized) ? normalized : ''
}

export function parseQueryState(query: LocationQuery): DocumentsQueryState {
  const rawPage = firstValue(query.page as string | string[] | undefined)
  const rawPageSize = firstValue(query.pageSize as string | string[] | undefined)

  const page = Math.max(0, parseIntegerParam(rawPage, DEFAULT_QUERY_STATE.page))
  const pageSize = Math.min(
    MAX_PAGE_SIZE,
    Math.max(
      MIN_PAGE_SIZE,
      parseIntegerParam(rawPageSize, DEFAULT_QUERY_STATE.pageSize),
    ),
  )

  return {
    page,
    pageSize,
    orderBy:
      firstValue(query.orderby as string | string[] | undefined) ||
      DEFAULT_QUERY_STATE.orderBy,
    search: firstValue(query.search as string | string[] | undefined),
    mediaType: parseMediaTypeParam(
      firstValue(query.mediaType as string | string[] | undefined),
    ),
    categoryId: firstValue(query.category as string | string[] | undefined),
    userId: firstValue(query.user as string | string[] | undefined),
    jobId: firstValue(query.jobId as string | string[] | undefined),
  }
}

export function serializeQueryState(
  state: DocumentsQueryState,
): Record<string, string> {
  const out: Record<string, string> = {}
  if (state.page !== DEFAULT_QUERY_STATE.page) out.page = String(state.page)
  if (state.pageSize !== DEFAULT_QUERY_STATE.pageSize) {
    out.pageSize = String(state.pageSize)
  }
  if (state.orderBy !== DEFAULT_QUERY_STATE.orderBy) out.orderby = state.orderBy
  if (state.search) out.search = state.search
  if (state.mediaType) out.mediaType = state.mediaType
  if (state.categoryId) out.category = state.categoryId
  if (state.userId) out.user = state.userId
  if (state.jobId) out.jobId = state.jobId
  return out
}
