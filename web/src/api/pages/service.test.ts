import { describe, it, expect, vi, beforeEach } from 'vitest'
import PageService from './service'

const VALID_UUID = '550e8400-e29b-41d4-a716-446655440000'

describe('PageService.getPageImage', () => {
  let service: PageService
  let getSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new PageService({ base: 'http://test', withCredentials: false })
    getSpy = vi.fn()
    ;(service as unknown as { instance: { get: typeof getSpy } }).instance = {
      get: getSpy,
    }
  })

  it.each([
    ['empty string', ''],
    ['plain text', 'not-a-uuid'],
    ['path traversal attempt', '../../../etc/passwd'],
    ['UUID-like but wrong length', '550e8400-e29b-41d4-a716-44665544000'],
    ['SQL injection attempt', "'; DROP TABLE pages--"],
  ])('rejects %s as pageId without calling the API', async (_label, id) => {
    await expect(service.getPageImage(id)).rejects.toThrow(/invalid page id/i)
    expect(getSpy).not.toHaveBeenCalled()
  })

  it('GETs /api/v1/pages/{id}/image with responseType blob', async () => {
    const blob = new Blob(['x'], { type: 'image/png' })
    getSpy.mockResolvedValue({
      data: blob,
      headers: { 'content-type': 'image/png' },
    })

    const result = await service.getPageImage(VALID_UUID)

    expect(getSpy).toHaveBeenCalledWith(`api/v1/pages/${VALID_UUID}/image`, {
      responseType: 'blob',
    })
    expect(result.blob).toBe(blob)
    expect(result.contentType).toBe('image/png')
  })

  describe('content-type header parsing', () => {
    const withHeaders = (headers: Record<string, unknown>) => ({
      data: new Blob(),
      headers,
    })

    it('returns string content-type as-is', async () => {
      getSpy.mockResolvedValue(withHeaders({ 'content-type': 'image/jpeg' }))
      const r = await service.getPageImage(VALID_UUID)
      expect(r.contentType).toBe('image/jpeg')
    })

    it.each([
      ['missing header', {}],
      ['undefined value', { 'content-type': undefined }],
      ['null value', { 'content-type': null }],
      ['empty string', { 'content-type': '' }],
      ['array value', { 'content-type': ['image/png'] }],
      ['numeric value', { 'content-type': 123 }],
      ['boolean value', { 'content-type': true }],
    ])(
      'falls back to application/octet-stream for %s',
      async (_label, headers) => {
        getSpy.mockResolvedValue(withHeaders(headers))
        const r = await service.getPageImage(VALID_UUID)
        expect(r.contentType).toBe('application/octet-stream')
      },
    )
  })
})
