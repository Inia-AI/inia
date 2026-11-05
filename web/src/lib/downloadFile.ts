import { problemDetailsMessage, type ProblemDetails } from '@/api/base/errors'

export function parseFilenameFromContentDisposition(
  header: string | undefined,
): string | null {
  if (!header) return null

  const rfc5987 = header.match(/filename\*=UTF-8''([^;]+)/i)
  if (rfc5987?.[1]) {
    try {
      return decodeURIComponent(rfc5987[1].trim())
    } catch {
      // fall through to the plain form
    }
  }

  const plain = header.match(/filename=("?)([^";]+)\1/i)
  if (plain?.[2]) {
    return plain[2].trim()
  }

  return null
}

// A blob-typed request wraps even the error body in a Blob, so the server's ProblemDetails
// must be read back out of it before any message can be shown. Unwrapping the Blob is the
// only work done here — the message pick is problemDetailsMessage, the same one every
// non-blob call site goes through. A non-JSON body resolves to undefined and the caller's
// localized fallback speaks instead.
export async function readBlobProblemDetail(error: unknown): Promise<string | undefined> {
  const data = (error as { response?: { data?: unknown } } | null)?.response?.data
  if (!(data instanceof Blob)) {
    return undefined
  }
  try {
    return problemDetailsMessage(JSON.parse(await data.text()) as ProblemDetails)
  } catch {
    return undefined
  }
}

// The anchor must be in the document for Firefox to honor the click, and the object URL
// must be revoked even when the click throws, or the blob stays referenced for the session.
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  document.body.appendChild(anchor)
  try {
    anchor.click()
  } finally {
    anchor.remove()
    URL.revokeObjectURL(url)
  }
}
