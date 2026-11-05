import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'

const { mutateAsyncMock, isPendingRef, toastMock } = vi.hoisted(() => ({
  mutateAsyncMock: vi.fn(),
  isPendingRef: { value: false },
  toastMock: vi.fn(),
}))

vi.mock('@/api', () => ({
  default: {
    ExecutedWorkflows: {
      useDownloadExecutedWorkflow: () => ({
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
import { useDownloadExecutedWorkflow } from './useDownloadExecutedWorkflow'
import type { DownloadExecutedWorkflowParams } from '@/api/executedWorkflows/schema'

async function runDownload(params: DownloadExecutedWorkflowParams) {
  let api: ReturnType<typeof useDownloadExecutedWorkflow> | null = null
  const Comp = defineComponent({
    setup() {
      api = useDownloadExecutedWorkflow()
      return () => h('div')
    },
  })
  const wrapper = mount(Comp)
  await api!.download(params)
  wrapper.unmount()
  return api!
}

describe('useDownloadExecutedWorkflow', () => {
  let createObjectURL: ReturnType<typeof vi.fn>
  let revokeObjectURL: ReturnType<typeof vi.fn>
  let clickSpy: ReturnType<typeof vi.fn> | null
  let lastAnchor: HTMLAnchorElement | null
  let clickBehavior: 'noop' | 'throw'
  let originalCreateObjectURL: unknown
  let originalRevokeObjectURL: unknown

  beforeEach(() => {
    vi.clearAllMocks()
    createObjectURL = vi.fn(() => 'blob:fake-url')
    revokeObjectURL = vi.fn()
    originalCreateObjectURL = (globalThis.URL as { createObjectURL?: unknown })
      .createObjectURL
    originalRevokeObjectURL = (globalThis.URL as { revokeObjectURL?: unknown })
      .revokeObjectURL
    Object.defineProperty(globalThis.URL, 'createObjectURL', {
      value: createObjectURL,
      configurable: true,
      writable: true,
    })
    Object.defineProperty(globalThis.URL, 'revokeObjectURL', {
      value: revokeObjectURL,
      configurable: true,
      writable: true,
    })

    clickSpy = null
    lastAnchor = null
    clickBehavior = 'noop'
    const realCreate = document.createElement.bind(document)
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const el = realCreate(tag as keyof HTMLElementTagNameMap)
      if (tag === 'a') {
        const anchor = el as HTMLAnchorElement
        lastAnchor = anchor
        clickSpy = vi.fn(() => {
          if (clickBehavior === 'throw') throw new Error('boom')
        })
        ;(anchor as unknown as { click: () => void }).click = clickSpy
      }
      return el
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    Object.defineProperty(globalThis.URL, 'createObjectURL', {
      value: originalCreateObjectURL,
      configurable: true,
      writable: true,
    })
    Object.defineProperty(globalThis.URL, 'revokeObjectURL', {
      value: originalRevokeObjectURL,
      configurable: true,
      writable: true,
    })
  })

  it('on success: creates object URL, clicks the link with the server filename, revokes, toasts success', async () => {
    const blob = new Blob(['{}'], { type: 'application/json' })
    mutateAsyncMock.mockResolvedValue({ blob, filename: 'My.workflow' })

    await runDownload({ executionId: 'exec-1' })

    expect(mutateAsyncMock).toHaveBeenCalledWith({ executionId: 'exec-1' })
    expect(createObjectURL).toHaveBeenCalledWith(blob)
    expect(clickSpy).toHaveBeenCalledOnce()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:fake-url')
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'shared.workflowDownload.downloadSuccess',
      }),
    )
  })

  it('sets the <a download> attribute to the filename from the service', async () => {
    mutateAsyncMock.mockResolvedValue({
      blob: new Blob(),
      filename: 'Report one.workflow',
    })

    await runDownload({ executionId: 'x' })

    expect(lastAnchor).not.toBeNull()
    expect(lastAnchor!.download).toBe('Report one.workflow')
  })

  it('on error with a problem+json Blob body: toasts destructive with the server message', async () => {
    // The API carries the actionable message in `title` (the same pick every non-blob
    // call site gets from extractApiErrorMessage); `detail` is the fallback.
    const problem = {
      title: 'Executed workflow not found.',
      detail: 'No such workflow',
      status: 404,
    }
    const errorBlob = new Blob([JSON.stringify(problem)], {
      type: 'application/problem+json',
    })
    mutateAsyncMock.mockRejectedValue({
      response: { data: errorBlob, status: 404 },
      message: 'Request failed with status code 404',
    })

    await runDownload({ executionId: 'missing' })

    expect(createObjectURL).not.toHaveBeenCalled()
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'shared.workflowDownload.downloadError',
        description: 'Executed workflow not found.',
        variant: 'destructive',
      }),
    )
  })

  it('on error without a parseable body: toasts destructive with generic description', async () => {
    mutateAsyncMock.mockRejectedValue(new Error('network down'))
    await runDownload({ jobId: 'job-x' })
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'shared.workflowDownload.downloadError',
        variant: 'destructive',
      }),
    )
  })

  it('revokes the object URL even if click throws', async () => {
    mutateAsyncMock.mockResolvedValue({
      blob: new Blob(),
      filename: 'x.workflow',
    })
    clickBehavior = 'throw'

    // download() must not rethrow — it falls through to the error toast.
    await runDownload({ executionId: 'x' })

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:fake-url')
  })
})
