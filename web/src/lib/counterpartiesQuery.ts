import type { LocationQuery } from 'vue-router'
import type { CounterpartyQueryParams } from '@/api/base/schema'
import { odataContainsAny, odataIn, quoteODataLiteral } from '@/utils/odata'

export interface CounterpartiesQueryState {
  page: number
  pageSize: number
  orderBy: string
  search: string
  typeIds: string[]
}

export const DEFAULT_QUERY_STATE: CounterpartiesQueryState = {
  page: 0,
  pageSize: 25,
  orderBy: 'name asc',
  search: '',
  typeIds: [],
}

export const MATCH_QUERY_CAP = 500

const MIN_PAGE_SIZE = 10
const MAX_PAGE_SIZE = 100

export const isFiltering = (state: CounterpartiesQueryState): boolean =>
  state.search.trim().length > 0 || state.typeIds.length > 0

export function buildRootsQuery(
  state: CounterpartiesQueryState,
): CounterpartyQueryParams {
  return {
    $skip: state.page * state.pageSize,
    $top: state.pageSize,
    $count: true,
    $orderby: state.orderBy,
    $filter: 'parentId eq null',
  }
}

export function buildMatchQuery(
  state: CounterpartiesQueryState,
): CounterpartyQueryParams {
  const params: CounterpartyQueryParams = {
    $skip: 0,
    $top: MATCH_QUERY_CAP,
    $count: true,
    $orderby: state.orderBy,
  }
  const search = odataContainsAny(['name', 'id', 'nameVariants'], state.search)
  if (search) {
    params.$filter = search
  }
  if (state.typeIds.length) {
    params.counterpartyTypeIds = state.typeIds.join(',')
  }
  return params
}

export function buildContextParentsQuery(
  ids: string[],
): CounterpartyQueryParams {
  if (!ids.length) return { $top: 0 }
  return { $top: ids.length, $filter: odataIn('id', ids) }
}

export function buildChildrenOfRootsQuery(
  rootIds: string[],
): CounterpartyQueryParams {
  if (!rootIds.length) return { $top: 0 }
  return {
    $top: 10000,
    $orderby: 'name asc',
    $filter: odataIn('parentId', rootIds),
  }
}

export function buildHasChildrenProbe(
  rootIds: string[],
): CounterpartyQueryParams {
  if (!rootIds.length) return { $top: 0 }
  return {
    $top: 10000,
    $select: 'parentId',
    $filter: odataIn('parentId', rootIds),
  }
}

export function buildSingleParentChildrenQuery(
  parentId: string,
): CounterpartyQueryParams {
  return {
    $top: 10000,
    $orderby: 'name asc',
    $filter: `parentId eq ${quoteODataLiteral(parentId)}`,
  }
}

const firstValue = (value: string | string[] | null | undefined): string => {
  if (Array.isArray(value)) return value[0] ?? ''
  return value ?? ''
}

const parseInteger = (raw: string, fallback: number): number => {
  if (!raw) return fallback
  const n = Number(raw)
  return Number.isFinite(n) && Number.isInteger(n) ? n : fallback
}

const parseIdList = (raw: string | string[] | null | undefined): string[] => {
  if (Array.isArray(raw)) {
    return raw
      .flatMap(v => (typeof v === 'string' ? v.split(',') : []))
      .filter(Boolean)
  }
  if (typeof raw === 'string' && raw) {
    return raw.split(',').filter(Boolean)
  }
  return []
}

export function parseQueryState(query: LocationQuery): CounterpartiesQueryState {
  const rawPage = firstValue(query.page as string | string[] | undefined)
  const rawPageSize = firstValue(query.pageSize as string | string[] | undefined)

  const page = Math.max(0, parseInteger(rawPage, DEFAULT_QUERY_STATE.page))
  const pageSize = Math.min(
    MAX_PAGE_SIZE,
    Math.max(
      MIN_PAGE_SIZE,
      parseInteger(rawPageSize, DEFAULT_QUERY_STATE.pageSize),
    ),
  )

  return {
    page,
    pageSize,
    orderBy:
      firstValue(query.orderby as string | string[] | undefined) ||
      DEFAULT_QUERY_STATE.orderBy,
    search: firstValue(query.search as string | string[] | undefined),
    typeIds: parseIdList(query.type as string | string[] | undefined),
  }
}

export function serializeQueryState(
  state: CounterpartiesQueryState,
): Record<string, string> {
  const out: Record<string, string> = {}
  if (state.page !== DEFAULT_QUERY_STATE.page) out.page = String(state.page)
  if (state.pageSize !== DEFAULT_QUERY_STATE.pageSize) {
    out.pageSize = String(state.pageSize)
  }
  if (state.orderBy !== DEFAULT_QUERY_STATE.orderBy) out.orderby = state.orderBy
  if (state.search) out.search = state.search
  if (state.typeIds.length) out.type = state.typeIds.join(',')
  return out
}

