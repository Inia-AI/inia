import { beforeEach, describe, expect, it, vi } from 'vitest'
import Service from './service'

describe('WorkflowReportsService.exportWorkflowReport', () => {
  let service: Service
  let getSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new Service({ base: 'http://test', withCredentials: false })
    getSpy = vi.fn()
    ;(service as unknown as { instance: { get: typeof getSpy } }).instance = { get: getSpy }
  })

  it.each([
    ['word', 'Credit Review - 2026-10-20.docx'],
    ['markdown', 'Credit Review - 2026-10-20.md'],
  ] as const)('GETs the %s export as a blob under the server-sent filename', async (format, filename) => {
    const blob = new Blob(['report'])
    getSpy.mockResolvedValue({
      data: blob,
      headers: { 'content-disposition': `attachment; filename="${filename}"` },
    })

    const result = await service.exportWorkflowReport('exec-1', format)

    expect(getSpy).toHaveBeenCalledWith(`api/v1/workflow-reports/exec-1/export/${format}`, {
      responseType: 'blob',
    })
    expect(result).toEqual({ blob, filename })
  })
})
