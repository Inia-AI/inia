export interface CounterpartyType {
  id: string
  name: string
  description?: string
  counterpartyCount: number
  isBuiltIn: boolean
}

export interface CounterpartyTypeRequest {
  name: string
  description?: string
}
