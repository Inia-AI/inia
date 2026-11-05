import type { OptionType } from '@/types'

export interface CounterpartyLite {
  id: string
  name: string
}

export interface ExecutionLite {
  id: string
  createdAt?: string
  title?: string | null
  counterpartyId?: string | null
  analystName?: string | null
}

export interface ExecutionLabelFormatters {
  fmtDate: (value: string | undefined) => string
  counterpartyLabel: (id: string | undefined | null) => string
}

export interface PlaceholderLabels {
  loading: string
  empty: string
  ready: string
}

export interface PlaceholderState {
  isLoading: boolean
  isEmpty: boolean
}

export function buildCounterpartyOptions(
  counterparties: readonly CounterpartyLite[],
  anyLabel: string,
): OptionType[] {
  return [
    { label: anyLabel, value: '' },
    ...counterparties.map(cp => ({ label: cp.name, value: cp.id })),
  ]
}

export function buildExecutionOptions(
  executions: readonly ExecutionLite[],
  fmt: ExecutionLabelFormatters,
): OptionType[] {
  return executions.map(exec => {
    const parts: string[] = []
    const date = fmt.fmtDate(exec.createdAt)
    if (date) parts.push(date)
    if (exec.title) parts.push(exec.title)
    if (exec.counterpartyId) {
      const name = fmt.counterpartyLabel(exec.counterpartyId)
      if (name) parts.push(name)
    }
    if (exec.analystName) parts.push(exec.analystName)
    return { label: parts.join(' · '), value: exec.id }
  })
}

export function pickExecutionPlaceholder(
  state: PlaceholderState,
  labels: PlaceholderLabels,
): string {
  if (state.isLoading) return labels.loading
  if (state.isEmpty) return labels.empty
  return labels.ready
}
