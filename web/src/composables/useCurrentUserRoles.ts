import {
  computed,
  getCurrentScope,
  onScopeDispose,
  ref,
  type ComputedRef,
} from 'vue'
import { hasAnyRole } from '@/lib/utils'
import { ADMIN_ROLES, EDITOR_ROLES, OPERATOR_ROLES, ROLES } from '@/constants/roles'

// One shared reactive tick across every consumer. Bumping it forces every
// mounted role check to re-read the token.
const tick = ref(0)

// One window-level `storage` listener shared across consumers, ref-counted so
// it detaches when the last caller unmounts. Without this, each row rendering
// `DownloadExecutedWorkflowButton` would register its own listener and cause
// N recomputations per storage event.
let listenerRefCount = 0
let storageListener: ((event: StorageEvent) => void) | null = null

function attachStorageListener() {
  if (typeof window === 'undefined') return
  listenerRefCount++
  if (storageListener) return
  storageListener = (event: StorageEvent) => {
    // null key = localStorage.clear(); everything else narrows to the auth token.
    if (event.key !== null && event.key !== 'accessToken') return
    tick.value++
  }
  window.addEventListener('storage', storageListener)
}

function detachStorageListener() {
  if (typeof window === 'undefined' || listenerRefCount === 0) return
  listenerRefCount--
  if (listenerRefCount > 0 || !storageListener) return
  window.removeEventListener('storage', storageListener)
  storageListener = null
}

export interface CurrentUserRoles {
  isSuperAdmin: ComputedRef<boolean>
  isAdmin: ComputedRef<boolean>
  isEditor: ComputedRef<boolean>
  isOperator: ComputedRef<boolean>
}

export function useCurrentUserRoles(): CurrentUserRoles {
  return {
    isSuperAdmin: useRoleCheck([ROLES.SUPER_ADMIN]),
    isAdmin: useRoleCheck(ADMIN_ROLES),
    // Mirrors the backend EDITOR_ROLES gate: deployment-wide authoring such as prompts and categories.
    isEditor: useRoleCheck(EDITOR_ROLES),
    // Mirrors the backend OPERATOR_ROLES gate: logs, executed-workflow downloads, pricing, clean-up.
    isOperator: useRoleCheck(OPERATOR_ROLES),
  }
}

export function useRoleCheck(roles: readonly string[]): ComputedRef<boolean> {
  // Only register the listener inside an effect scope so cleanup actually runs.
  // Outside a scope (e.g. a plain module import) onScopeDispose is a no-op.
  if (getCurrentScope()) {
    attachStorageListener()
    onScopeDispose(detachStorageListener)
  }

  return computed(() => {
    void tick.value
    return hasAnyRole(roles)
  })
}
