export interface ServiceParams {
  base: string
  withCredentials: boolean
}

export interface ODataResponse<T> {
  '@odata.count': number
  '@odata.skip': number
  '@odata.top': number
  value: T[]
}

export interface OData<T> {
  count: number
  skip: number
  top: number
  value: T[]
}

export interface ODataQueryParams {
  $skip?: number
  $top?: number
  $filter?: string
  $orderby?: string
  $select?: string
  $expand?: string
  $count?: boolean
}

export interface CounterpartyQueryParams extends ODataQueryParams {
  counterpartyTypeId?: string
  counterpartyTypeIds?: string
}

export interface ListResponse<T> {
  value: T[]
}
