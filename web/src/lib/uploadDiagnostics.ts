import type { UploadErrorCode } from '@/composables/useDocumentUpload'

export interface FailedUploadInfo {
  name: string
  sizeBytes: number
  error: UploadErrorCode
}

export interface UploadDiagnosticsContext {
  pageUrl: string
  uploadEndpoint: string
  userAgent: string
  timestamp: string
}

const ERROR_SUMMARIES: Record<UploadErrorCode, string> = {
  blockedBeforeServer:
    'no response received - the connection was closed before the server answered (typically endpoint security software or a proxy)',
  generic: 'the server rejected the upload',
  refused: 'the server refused the file: its bytes are not a document the platform reads',
  passwordRequired: 'the file is password-protected and no password was supplied',
  passwordIncorrect: 'the supplied password was incorrect',
  passwordUnsupported: 'the file uses certificate- or DRM-based protection that cannot be removed',
  limited: "the organization's storage limit or hourly upload limit was reached",
}

export function buildUploadDiagnostics(
  failed: FailedUploadInfo[],
  ctx: UploadDiagnosticsContext,
): string {
  return [
    'INIA upload failure diagnostics',
    `Time: ${ctx.timestamp}`,
    `Page: ${ctx.pageUrl}`,
    `Upload endpoint: ${ctx.uploadEndpoint}`,
    `Browser: ${ctx.userAgent}`,
    'Failed files:',
    ...failed.map(f => `- ${f.name} (${f.sizeBytes} bytes): ${ERROR_SUMMARIES[f.error]}`),
  ].join('\n')
}
