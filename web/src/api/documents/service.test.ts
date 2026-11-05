import { describe, it, expect, vi, beforeEach } from 'vitest'
import Service from './service'

describe('DocumentsService', () => {
  let service: Service
  let getSpy: ReturnType<typeof vi.fn>
  let postSpy: ReturnType<typeof vi.fn>
  let deleteSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new Service()
    getSpy = vi.fn()
    postSpy = vi.fn()
    deleteSpy = vi.fn()
    ;(service as unknown as {
      instance: { get: typeof getSpy; post: typeof postSpy; delete: typeof deleteSpy }
    }).instance = { get: getSpy, post: postSpy, delete: deleteSpy }
  })

  describe('getDocuments', () => {
    it('GETs /api/v1/documents with the supplied params and normalises the OData envelope', async () => {
      getSpy.mockResolvedValue({
        data: {
          value: [{ id: 'd1' }],
          '@odata.count': 1,
          '@odata.skip': 0,
          '@odata.top': 25,
        },
      })

      const result = await service.getDocuments({
        $skip: 0,
        $top: 25,
        $count: true,
        jobId: 'x',
      })

      expect(getSpy).toHaveBeenCalledWith('api/v1/documents', {
        params: { $skip: 0, $top: 25, $count: true, jobId: 'x' },
      })
      expect(result).toEqual({
        value: [{ id: 'd1' }],
        count: 1,
        skip: 0,
        top: 25,
      })
    })

    it('returns null when the request fails', async () => {
      getSpy.mockRejectedValue(new Error('boom'))
      const result = await service.getDocuments({ $skip: 0, $top: 25 })
      expect(result).toBeNull()
    })
  })

  describe('getDocument', () => {
    it('GETs /api/v1/documents/{id} and returns the body', async () => {
      const detail = { id: 'd1', name: 'x.pdf' }
      getSpy.mockResolvedValue({ data: detail })
      const result = await service.getDocument('d1')
      expect(getSpy).toHaveBeenCalledWith('api/v1/documents/d1')
      expect(result).toBe(detail)
    })

    it('lets axios errors propagate (useMutation handles them)', async () => {
      getSpy.mockRejectedValue(new Error('nope'))
      await expect(service.getDocument('d1')).rejects.toThrow('nope')
    })
  })

  describe('deleteDocument', () => {
    it('DELETEs /api/v1/documents/{id}', async () => {
      deleteSpy.mockResolvedValue({ data: null })
      await service.deleteDocument('d1')
      expect(deleteSpy).toHaveBeenCalledWith('api/v1/documents/d1')
    })
  })

  describe('bulkDelete', () => {
    it('POSTs /api/v1/documents/bulk-delete with { ids } and returns the body', async () => {
      const body = { deleted: ['a'], failed: { b: 'not found' } }
      postSpy.mockResolvedValue({ data: body })

      const result = await service.bulkDelete(['a', 'b'])

      expect(postSpy).toHaveBeenCalledWith('api/v1/documents/bulk-delete', {
        ids: ['a', 'b'],
      })
      expect(result).toEqual(body)
    })
  })

  describe('getDocumentPages', () => {
    it('GETs /api/v1/documents/{id}/pages with OData params', async () => {
      getSpy.mockResolvedValue({
        data: {
          value: [{ id: 'p1' }],
          '@odata.count': 1,
          '@odata.skip': 0,
          '@odata.top': 50,
        },
      })

      const result = await service.getDocumentPages('d1', { $top: 50 })

      expect(getSpy).toHaveBeenCalledWith('api/v1/documents/d1/pages', {
        params: { $top: 50 },
      })
      expect(result).toEqual({
        value: [{ id: 'p1' }],
        count: 1,
        skip: 0,
        top: 50,
      })
    })

    it('returns null when the request fails', async () => {
      getSpy.mockRejectedValue(new Error('boom'))
      expect(await service.getDocumentPages('d1', {})).toBeNull()
    })
  })
})
