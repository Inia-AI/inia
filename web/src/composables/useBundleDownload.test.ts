import { describe, expect, it, vi, beforeEach } from 'vitest'
import { ref } from 'vue'

const { downloadBlobMock, readProblemMock, toastMock } = vi.hoisted(() => ({
  downloadBlobMock: vi.fn(),
  readProblemMock: vi.fn(),
  toastMock: vi.fn(),
}))

vi.mock('@/lib/downloadFile', () => ({
  downloadBlob: downloadBlobMock,
  readBlobProblemDetail: readProblemMock,
}))

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock, toasts: [], dismiss: () => {} }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import { useBundleDownload } from './useBundleDownload'

const exportOf = (mutateAsync: ReturnType<typeof vi.fn>) => ({
  mutateAsync,
  isPending: ref(false),
})

describe('useBundleDownload', () => {
  beforeEach(() => {
    downloadBlobMock.mockReset()
    readProblemMock.mockReset()
    toastMock.mockReset()
  })

  it('downloads under the name the server gave the file, and says it did', async () => {
    const mutateAsync = vi
      .fn()
      .mockResolvedValue({
        blob: new Blob(['{}']),
        filename: 'counterparties Acme.json',
      })
    const { download } = useBundleDownload(
      exportOf(mutateAsync),
      'counterparties.json',
      'page.x.exportError',
    )

    expect(await download('arg-1')).toBe(true)
    expect(mutateAsync).toHaveBeenCalledWith('arg-1')
    expect(downloadBlobMock).toHaveBeenCalledWith(
      expect.any(Blob),
      'counterparties Acme.json',
    )
    expect(toastMock).not.toHaveBeenCalled()
  })

  it('falls back to the given name when the server names nothing', async () => {
    const mutateAsync = vi
      .fn()
      .mockResolvedValue({ blob: new Blob(['{}']), filename: null })
    const { download } = useBundleDownload(
      exportOf(mutateAsync),
      'counterparties.json',
      'page.x.exportError',
    )

    await download(undefined)

    expect(downloadBlobMock).toHaveBeenCalledWith(
      expect.any(Blob),
      'counterparties.json',
    )
  })

  it('speaks the server problem when the export is refused, else the page fallback, and says it failed', async () => {
    const mutateAsync = vi.fn().mockRejectedValue(new Error('403'))
    const { download } = useBundleDownload(
      exportOf(mutateAsync),
      'x.zip',
      'page.x.exportError',
    )

    readProblemMock.mockResolvedValueOnce('Only an administrator can export.')
    expect(await download('arg-1')).toBe(false)
    expect(toastMock).toHaveBeenLastCalledWith(
      expect.objectContaining({
        variant: 'destructive',
        description: 'Only an administrator can export.',
      }),
    )

    readProblemMock.mockResolvedValueOnce(undefined)
    await download('arg-1')
    expect(toastMock).toHaveBeenLastCalledWith(
      expect.objectContaining({
        variant: 'destructive',
        description: 'page.x.exportError',
      }),
    )
    expect(downloadBlobMock).not.toHaveBeenCalled()
  })
})
