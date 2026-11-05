import { describe, it, expect, afterEach, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useCurrentUserRoles, useRoleCheck } from './useCurrentUserRoles'
import { setAuthToken, clearAuthToken } from '@/test/helpers/jwt'

afterEach(() => {
  clearAuthToken()
})

// Mount the composable inside a real component so the onScopeDispose cleanup
// runs and Vue tracks the computed refs properly.
function mountComposable() {
  let api: ReturnType<typeof useCurrentUserRoles> | null = null
  const Comp = defineComponent({
    setup() {
      api = useCurrentUserRoles()
      return () => h('div')
    },
  })
  const wrapper = mount(Comp)
  return { api: api!, wrapper }
}

describe('useCurrentUserRoles', () => {
  it('reports false for all roles when no token is stored', () => {
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(false)
    expect(api.isAdmin.value).toBe(false)
    wrapper.unmount()
  })

  it('reports isSuperAdmin when the token carries SuperAdmin', () => {
    setAuthToken({ role: 'SuperAdmin' })
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(true)
    expect(api.isAdmin.value).toBe(true) // Admin is inclusive of SuperAdmin
    wrapper.unmount()
  })

  it('reports isAdmin but not isSuperAdmin for plain Admin', () => {
    setAuthToken({ role: 'Admin' })
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(false)
    expect(api.isAdmin.value).toBe(true)
    wrapper.unmount()
  })

  it('reports isEditor for WorkflowEditor and SuperAdmin, not for Admin or Operator', () => {
    setAuthToken({ role: 'WorkflowEditor' })
    let mounted = mountComposable()
    expect(mounted.api.isEditor.value).toBe(true)
    mounted.wrapper.unmount()

    setAuthToken({ role: 'SuperAdmin' })
    mounted = mountComposable()
    expect(mounted.api.isEditor.value).toBe(true)
    mounted.wrapper.unmount()

    setAuthToken({ role: ['Admin', 'Operator'] })
    mounted = mountComposable()
    expect(mounted.api.isEditor.value).toBe(false)
    mounted.wrapper.unmount()
  })

  it('reports isOperator for Operator and SuperAdmin, not for Admin or WorkflowEditor', () => {
    setAuthToken({ role: 'Operator' })
    let mounted = mountComposable()
    expect(mounted.api.isOperator.value).toBe(true)
    expect(mounted.api.isSuperAdmin.value).toBe(false)
    mounted.wrapper.unmount()

    setAuthToken({ role: 'SuperAdmin' })
    mounted = mountComposable()
    expect(mounted.api.isOperator.value).toBe(true)
    mounted.wrapper.unmount()

    setAuthToken({ role: ['Admin', 'WorkflowEditor'] })
    mounted = mountComposable()
    expect(mounted.api.isOperator.value).toBe(false)
    mounted.wrapper.unmount()
  })

  it('handles roles stored as an array', () => {
    setAuthToken({ role: ['User', 'SuperAdmin'] })
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(true)
    wrapper.unmount()
  })

  it('recomputes after a cross-tab storage event updates the token', async () => {
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(false)

    setAuthToken({ role: 'SuperAdmin' })
    window.dispatchEvent(new StorageEvent('storage', { key: 'accessToken' }))
    await nextTick()

    expect(api.isSuperAdmin.value).toBe(true)
    wrapper.unmount()
  })

  it('recomputes a lone role check after a cross-tab storage event updates the token', async () => {
    let isOperator: ReturnType<typeof useRoleCheck> | null = null
    const wrapper = mount(
      defineComponent({
        setup() {
          isOperator = useRoleCheck(['Operator'])
          return () => h('div')
        },
      }),
    )
    expect(isOperator!.value).toBe(false)

    setAuthToken({ role: 'Operator' })
    window.dispatchEvent(new StorageEvent('storage', { key: 'accessToken' }))
    await nextTick()

    expect(isOperator!.value).toBe(true)
    wrapper.unmount()
  })

  it('ignores storage events for keys other than accessToken', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const { api, wrapper } = mountComposable()
    expect(api.isSuperAdmin.value).toBe(true)

    // Simulate an unrelated key being written in another tab. The composable
    // should not recompute, and (more importantly) should not throw.
    clearAuthToken()
    window.dispatchEvent(new StorageEvent('storage', { key: 'somethingElse' }))
    await nextTick()

    // The underlying token is gone, but because we ignored the event and
    // didn't bump `tick`, the cached computed still reports the last value.
    expect(api.isSuperAdmin.value).toBe(true)
    wrapper.unmount()
  })

  it('shares a single window listener across all consumers', () => {
    const addSpy = vi.spyOn(window, 'addEventListener')
    const removeSpy = vi.spyOn(window, 'removeEventListener')

    const a = mountComposable()
    const b = mountComposable()
    const c = mountComposable()

    const storageAdds = addSpy.mock.calls.filter(([type]) => type === 'storage')
    expect(storageAdds).toHaveLength(1)

    a.wrapper.unmount()
    b.wrapper.unmount()

    // Two consumers still mounted — listener must stay.
    expect(
      removeSpy.mock.calls.filter(([type]) => type === 'storage'),
    ).toHaveLength(0)

    c.wrapper.unmount()

    // Last consumer unmounted — listener must be cleaned up.
    expect(
      removeSpy.mock.calls.filter(([type]) => type === 'storage'),
    ).toHaveLength(1)

    addSpy.mockRestore()
    removeSpy.mockRestore()
  })
})
