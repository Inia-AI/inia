import {
  computed,
  getCurrentScope,
  onScopeDispose,
  ref,
  type ComputedRef,
} from 'vue'
import { getDecodedToken, tokenUserId } from '@/lib/utils'

// Shared reactive tick across every consumer. Bumping it forces every mounted
// `useCurrentUserId()` computed to re-decode the JWT. Mirrors the pattern in
// `useCurrentUserRoles` so storage events (login / logout in another tab)
// invalidate the cached value without needing per-component listeners.
const tick = ref(0)

let listenerRefCount = 0
let storageListener: ((event: StorageEvent) => void) | null = null

function attachStorageListener() {
  if (typeof window === 'undefined') {
    return
  }
  listenerRefCount++
  if (storageListener) {
    return
  }
  storageListener = (event: StorageEvent) => {
    // null key = localStorage.clear(); everything else narrows to the auth token.
    if (event.key !== null && event.key !== 'accessToken') {
      return
    }
    tick.value++
  }
  window.addEventListener('storage', storageListener)
}

function detachStorageListener() {
  if (typeof window === 'undefined' || listenerRefCount === 0) {
    return
  }
  listenerRefCount--
  if (listenerRefCount > 0 || !storageListener) {
    return
  }
  window.removeEventListener('storage', storageListener)
  storageListener = null
}

/**
 * Reactive accessor for the current user's id, decoded from the JWT.
 *
 * Tries the standard claim names in order: `sub`, `user_id`, `userId`, `id`.
 * Returns `null` when no token is present or the token can't be decoded.
 *
 * Mirrors the listener / ref-count pattern in `useCurrentUserRoles` so a
 * login / logout in another tab invalidates every consumer at once.
 */
export function useCurrentUserId(): { userId: ComputedRef<string | null> } {
  if (getCurrentScope()) {
    attachStorageListener()
    onScopeDispose(detachStorageListener)
  }

  const userId = computed<string | null>(() => {
    void tick.value
    return tokenUserId(getDecodedToken())
  })

  return { userId }
}
