import { describe, it, expect, vi, beforeEach } from 'vitest'
import Service from './service'

describe('ExecutedWorkflowsService.downloadExecutedWorkflow', () => {
  let service: Service
  let getSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new Service({ base: 'http://test', withCredentials: false })
    getSpy = vi.fn()
    // Replace the axios instance with a minimal stub — we only need .get here.
    ;(service as unknown as { instance: { get: typeof getSpy } }).instance = {
      get: getSpy,
    }
  })

  it('rejects when no identifier is supplied', async () => {
    await expect(service.downloadExecutedWorkflow({})).rejects.toThrow(
      /exactly one/i,
    )
    expect(getSpy).not.toHaveBeenCalled()
  })

  it('rejects when more than one identifier is supplied', async () => {
    await expect(
      service.downloadExecutedWorkflow({ executionId: 'a', jobId: 'b' }),
    ).rejects.toThrow(/exactly one/i)
    expect(getSpy).not.toHaveBeenCalled()
  })

  it('GETs /api/v1/executed-workflows/download with executionId as a blob', async () => {
    const blob = new Blob(['{}'], { type: 'application/json' })
    getSpy.mockResolvedValue({
      data: blob,
      headers: { 'content-disposition': 'attachment; filename="My.workflow"' },
    })

    const result = await service.downloadExecutedWorkflow({
      executionId: 'exec-1',
    })

    expect(getSpy).toHaveBeenCalledWith(
      'api/v1/executed-workflows/download',
      {
        params: { executionId: 'exec-1' },
        responseType: 'blob',
      },
    )
    expect(result.filename).toBe('My.workflow')
    expect(result.blob).toBe(blob)
  })

  it('GETs with only workflowId when that is supplied', async () => {
    getSpy.mockResolvedValue({ data: new Blob(), headers: {} })
    await service.downloadExecutedWorkflow({ workflowId: 'wf-1' })
    expect(getSpy).toHaveBeenCalledWith(
      'api/v1/executed-workflows/download',
      {
        params: { workflowId: 'wf-1' },
        responseType: 'blob',
      },
    )
  })

  it('GETs with only jobId when that is supplied', async () => {
    getSpy.mockResolvedValue({ data: new Blob(), headers: {} })
    await service.downloadExecutedWorkflow({ jobId: 'job-1' })
    expect(getSpy).toHaveBeenCalledWith(
      'api/v1/executed-workflows/download',
      {
        params: { jobId: 'job-1' },
        responseType: 'blob',
      },
    )
  })

  describe('Content-Disposition parsing', () => {
    const withHeader = (h: string) => ({
      data: new Blob(),
      headers: { 'content-disposition': h },
    })

    it('parses plain quoted filename=', async () => {
      getSpy.mockResolvedValue(
        withHeader('attachment; filename="My Workflow.workflow"'),
      )
      const r = await service.downloadExecutedWorkflow({ executionId: 'x' })
      expect(r.filename).toBe('My Workflow.workflow')
    })

    it('parses unquoted filename=', async () => {
      getSpy.mockResolvedValue(withHeader('attachment; filename=plain.workflow'))
      const r = await service.downloadExecutedWorkflow({ executionId: 'x' })
      expect(r.filename).toBe('plain.workflow')
    })

    it("prefers RFC 5987 filename*=UTF-8''... over filename=", async () => {
      getSpy.mockResolvedValue(
        withHeader(
          `attachment; filename="fallback.workflow"; filename*=UTF-8''%C3%A9clair.workflow`,
        ),
      )
      const r = await service.downloadExecutedWorkflow({ executionId: 'x' })
      expect(r.filename).toBe('éclair.workflow')
    })

    it('falls back to executed-workflow-{id}.workflow when header is missing', async () => {
      getSpy.mockResolvedValue({ data: new Blob(), headers: {} })
      const r = await service.downloadExecutedWorkflow({
        executionId: 'exec-42',
      })
      expect(r.filename).toBe('executed-workflow-exec-42.workflow')
    })

    it('uses workflowId in the fallback when only workflowId is supplied', async () => {
      getSpy.mockResolvedValue({ data: new Blob(), headers: {} })
      const r = await service.downloadExecutedWorkflow({ workflowId: 'wf-99' })
      expect(r.filename).toBe('executed-workflow-wf-99.workflow')
    })

    it('uses jobId in the fallback when only jobId is supplied', async () => {
      getSpy.mockResolvedValue({ data: new Blob(), headers: {} })
      const r = await service.downloadExecutedWorkflow({ jobId: 'job-7' })
      expect(r.filename).toBe('executed-workflow-job-7.workflow')
    })

    it('reads the capitalised Content-Disposition header too', async () => {
      getSpy.mockResolvedValue({
        data: new Blob(),
        headers: { 'Content-Disposition': 'attachment; filename="Caps.workflow"' },
      })
      const r = await service.downloadExecutedWorkflow({ executionId: 'x' })
      expect(r.filename).toBe('Caps.workflow')
    })
  })
})
