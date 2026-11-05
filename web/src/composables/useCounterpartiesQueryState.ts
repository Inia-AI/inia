import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded } from 'vue-router'

import {
  DEFAULT_QUERY_STATE,
  parseQueryState,
  serializeQueryState,
  type CounterpartiesQueryState,
} from '@/lib/counterpartiesQuery'

export interface UseCounterpartiesQueryStateReturn {
  state: ComputedRef<CounterpartiesQueryState>
  patchState: (partial: Partial<CounterpartiesQueryState>) => Promise<void>
}

const shallowEqualRecord = (
  a: Record<string, string>,
  b: Record<string, string>,
): boolean => {
  const ak = Object.keys(a)
  const bk = Object.keys(b)
  if (ak.length !== bk.length) return false
  for (const k of ak) if (a[k] !== b[k]) return false
  return true
}

export function useCounterpartiesQueryState(): UseCounterpartiesQueryStateReturn {
  const route = useRoute() as RouteLocationNormalizedLoaded
  const router = useRouter()

  const state = computed<CounterpartiesQueryState>(() =>
    parseQueryState(route.query),
  )

  const patchState = async (partial: Partial<CounterpartiesQueryState>) => {
    const next = serializeQueryState({ ...state.value, ...partial })
    const current = serializeQueryState(state.value)
    if (shallowEqualRecord(next, current)) return
    await router.replace({ query: next })
  }

  return { state, patchState }
}

export { DEFAULT_QUERY_STATE }
