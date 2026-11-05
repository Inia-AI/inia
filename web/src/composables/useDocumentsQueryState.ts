import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded } from 'vue-router'

import {
  DEFAULT_QUERY_STATE,
  parseQueryState,
  serializeQueryState,
  type DocumentsQueryState,
} from '@/lib/documentsQuery'

export interface UseDocumentsQueryStateReturn {
  state: ComputedRef<DocumentsQueryState>
  patchState: (partial: Partial<DocumentsQueryState>) => Promise<void>
}

/**
 * URL is the source of truth for the Documents list filters/sort/page.
 * The returned `state` is a reactive view over `useRoute().query`; `patchState`
 * replaces the current route with an elided-defaults serialization so the URL
 * stays tidy (and shareable).
 */
export function useDocumentsQueryState(): UseDocumentsQueryStateReturn {
  const route = useRoute() as RouteLocationNormalizedLoaded
  const router = useRouter()

  const state = computed<DocumentsQueryState>(() => parseQueryState(route.query))

  const patchState = async (partial: Partial<DocumentsQueryState>) => {
    const next: DocumentsQueryState = { ...state.value, ...partial }
    await router.replace({ query: serializeQueryState(next) })
  }

  return { state, patchState }
}

export { DEFAULT_QUERY_STATE }
