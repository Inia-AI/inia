import { describe, it, expect } from 'vitest'
import { buildUploadDiagnostics } from './uploadDiagnostics'

describe('buildUploadDiagnostics', () => {
  it('renders context and one line per failed file', () => {
    const text = buildUploadDiagnostics(
      [
        { name: 'report.pdf', sizeBytes: 1029697, error: 'blockedBeforeServer' },
        { name: 'data.csv', sizeBytes: 232, error: 'generic' },
      ],
      {
        pageUrl: 'http://intranet-host:8080/workflows/create',
        uploadEndpoint: 'http://intranet-host:8080/api/v1/files',
        userAgent: 'TestBrowser/1.0',
        timestamp: '2026-07-03T14:13:20.000Z',
      },
    )

    expect(text).toContain('Time: 2026-07-03T14:13:20.000Z')
    expect(text).toContain('Page: http://intranet-host:8080/workflows/create')
    expect(text).toContain('Upload endpoint: http://intranet-host:8080/api/v1/files')
    expect(text).toContain('Browser: TestBrowser/1.0')
    expect(text).toContain('report.pdf (1029697 bytes)')
    expect(text).toContain('no response received')
    expect(text).toContain('data.csv (232 bytes)')
    expect(text).toContain('server rejected')
  })
})
