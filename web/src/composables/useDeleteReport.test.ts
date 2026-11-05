import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'

const { mutateAsyncMock, isPendingRef, toastMock } = vi.hoisted(() => ({
  mutateAsyncMock: vi.fn(),
  isPendingRef: { value: false },
  toastMock: vi.fn(),
}))

vi.mock('@/api', () => ({
  default: {
    WorkflowReports: {
      useDeleteWorkflowReport: () => ({
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

import { useDeleteReport } from './useDeleteReport'

interface CompApi {
  api: ReturnType<typeof useDeleteReport>
  unmount: () => void
}

function setup(options?: Parameters<typeof useDeleteReport>[0]): CompApi {
  let api: ReturnType<typeof useDeleteReport> | null = null
  const Comp = defineComponent({
    setup() {
      api = useDeleteReport(options)
      return () => h('div')
    },
  })
  const wrapper = mount(Comp)
  return { api: api!, unmount: () => wrapper.unmount() }
}

describe('useDeleteReport', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    isPendingRef.value = false
  })

  it('prompt() captures the target and opens the dialog', () => {
    const { api, unmount } = setup()

    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    api.prompt({ id: 'rep-1', title: 'Q4 review' })

    expect(api.showDialog.value).toBe(true)
    expect(api.target.value).toEqual({ id: 'rep-1', title: 'Q4 review' })

    unmount()
  })

  it('confirm() calls the mutation, toasts success, fires onSuccess, and dismisses', async () => {
    mutateAsyncMock.mockResolvedValue(undefined)
    const onSuccess = vi.fn()
    const { api, unmount } = setup({ onSuccess })

    const target = { id: 'rep-2', title: 'AES corp' }
    api.prompt(target)
    await api.confirm()

    expect(mutateAsyncMock).toHaveBeenCalledWith('rep-2')
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'shared.deleteReport.success' }),
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

  it('on mutation rejection: toasts destructive and dismisses without firing onSuccess', async () => {
    mutateAsyncMock.mockRejectedValue(new Error('500'))
    const onSuccess = vi.fn()
    const { api, unmount } = setup({ onSuccess })

    api.prompt({ id: 'rep-3' })
    await api.confirm()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'shared.deleteReport.error',
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

    api.prompt({ id: 'rep-4' })
    api.dismiss()

    expect(mutateAsyncMock).not.toHaveBeenCalled()
    expect(api.showDialog.value).toBe(false)
    expect(api.target.value).toBe(null)

    unmount()
  })

  it('prompt() is a no-op while a previous delete is still in flight', () => {
    // Mirrors the cancel guard: the dialog closes the moment the user
    // confirms, so without this check a user could open a second prompt
    // before the first DELETE settles and the original target would be
    // overwritten.
    const { api, unmount } = setup()

    api.prompt({ id: 'rep-first', title: 'First' })
    isPendingRef.value = true

    api.prompt({ id: 'rep-second', title: 'Second' })

    expect(api.target.value).toEqual({ id: 'rep-first', title: 'First' })
    expect(api.showDialog.value).toBe(true)

    unmount()
  })
})
