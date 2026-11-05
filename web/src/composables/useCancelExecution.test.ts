import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { WorkflowState } from '@/enums'

const { mutateAsyncMock, isPendingRef, toastMock } = vi.hoisted(() => ({
  mutateAsyncMock: vi.fn(),
  isPendingRef: { value: false },
  toastMock: vi.fn(),
}))

vi.mock('@/api', () => ({
  default: {
    WorkflowExecutions: {
      useCancelWorkflowExecution: () => ({
        mutateAsync: mutateAsyncMock,
        isPending: isPendingRef,
      }),
    },
  },
}))

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock, toasts: [], dismiss: () => {} }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

// Import after the mocks.
import {
  useCancelExecution,
  isCancellableState,
  CANCELLABLE_STATES,
} from './useCancelExecution'

interface CompApi {
  api: ReturnType<typeof useCancelExecution>
  unmount: () => void
}

function setup(options?: Parameters<typeof useCancelExecution>[0]): CompApi {
  let api: ReturnType<typeof useCancelExecution> | null = null
  const Comp = defineComponent({
    setup() {
      api = useCancelExecution(options)
      return () => h('div')
    },
  })
  const wrapper = mount(Comp)
  return { api: api!, unmount: () => wrapper.unmount() }
}

describe('isCancellableState', () => {
  it('returns true for in-flight states', () => {
    expect(isCancellableState(WorkflowState.Running)).toBe(true)
    expect(isCancellableState(WorkflowState.ProcessingDocuments)).toBe(true)
    expect(isCancellableState(WorkflowState.Queued)).toBe(true)
  })

  it('returns false for terminal or undefined states', () => {
    expect(isCancellableState(WorkflowState.Confirmed)).toBe(false)
    expect(isCancellableState(WorkflowState.Draft)).toBe(false)
    expect(isCancellableState(WorkflowState.Failed)).toBe(false)
    expect(isCancellableState(WorkflowState.Cancelled)).toBe(false)
    expect(isCancellableState(WorkflowState.Undefined)).toBe(false)
    expect(isCancellableState(undefined)).toBe(false)
  })

  it('exposes the state set so callers can derive other gates', () => {
    expect(CANCELLABLE_STATES.has(WorkflowState.Running)).toBe(true)
    expect(CANCELLABLE_STATES.has(WorkflowState.Draft)).toBe(false)
  })
})

describe('useCancelExecution', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    isPendingRef.value = false
  })

  it('prompt() captures the target and opens the dialog', () => {
    const { api, unmount } = setup()

    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    api.prompt({ id: 'exec-1', title: 'Q4 review' })

    expect(api.showDialog.value).toBe(true)
    expect(api.target.value).toEqual({ id: 'exec-1', title: 'Q4 review' })

    unmount()
  })

  it('confirm() calls the mutation, toasts success, fires onSuccess, and dismisses', async () => {
    mutateAsyncMock.mockResolvedValue(undefined)
    const onSuccess = vi.fn()
    const { api, unmount } = setup({ onSuccess })

    const target = { id: 'exec-2', title: 'AES corp' }
    api.prompt(target)
    await api.confirm()

    expect(mutateAsyncMock).toHaveBeenCalledWith('exec-2')
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'shared.cancelExecution.success' }),
    )
    expect(onSuccess).toHaveBeenCalledWith(target)
    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    unmount()
  })

  it('confirm() with no prior prompt is a no-op', async () => {
    const { api, unmount } = setup()

    await api.confirm()

    expect(mutateAsyncMock).not.toHaveBeenCalled()
    expect(toastMock).not.toHaveBeenCalled()
    expect(api.showDialog.value).toBe(false)

    unmount()
  })

  it('on mutation rejection: toasts destructive and dismisses without calling onSuccess', async () => {
    mutateAsyncMock.mockRejectedValue(new Error('500'))
    const onSuccess = vi.fn()
    const { api, unmount } = setup({ onSuccess })

    api.prompt({ id: 'exec-3' })
    await api.confirm()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'shared.cancelExecution.error',
        variant: 'destructive',
      }),
    )
    expect(onSuccess).not.toHaveBeenCalled()
    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    unmount()
  })

  it('dismiss() clears state without calling the mutation', () => {
    const { api, unmount } = setup()

    api.prompt({ id: 'exec-4' })
    api.dismiss()

    expect(mutateAsyncMock).not.toHaveBeenCalled()
    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    unmount()
  })

  it('prompt() is a no-op while a previous cancel is still in flight', () => {
    // Simulate a DELETE in flight by setting the mutation's isPending ref.
    // The composable must reject re-entry so the original target is not
    // overwritten and the eventual `finally { dismiss() }` settles the
    // dialog cleanly for the original call.
    const { api, unmount } = setup()

    api.prompt({ id: 'exec-first', title: 'First' })
    isPendingRef.value = true

    api.prompt({ id: 'exec-second', title: 'Second' })

    expect(api.target.value).toEqual({ id: 'exec-first', title: 'First' })
    expect(api.showDialog.value).toBe(true)

    unmount()
  })
})
