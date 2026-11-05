import { describe, it, expect, vi, beforeEach } from 'vitest'
import PeerCandidatesService from './service'
import { PEER_CANDIDATE_PAGE_SIZE } from './schema'

describe('PeerCandidatesService.getPeerCandidates', () => {
  let service: PeerCandidatesService
  let getSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    service = new PeerCandidatesService({ base: 'http://test', withCredentials: false })
    getSpy = vi.fn()
    ;(service as unknown as { instance: { get: typeof getSpy } }).instance = { get: getSpy }
  })

  it('GETs the peer candidates route with the workflow type and search', async () => {
    getSpy.mockResolvedValue({ data: [] })

    await service.getPeerCandidates({ workflowTypeId: 'wf-1', search: 'acme' })

    expect(getSpy).toHaveBeenCalledWith('api/v1/counterparties/peer-candidates', {
      params: { workflowTypeId: 'wf-1', search: 'acme', top: PEER_CANDIDATE_PAGE_SIZE },
    })
  })

  it('omits a blank search rather than sending an empty filter', async () => {
    getSpy.mockResolvedValue({ data: [] })

    await service.getPeerCandidates({ workflowTypeId: 'wf-1', search: '   ' })

    expect(getSpy).toHaveBeenCalledWith('api/v1/counterparties/peer-candidates', {
      params: { workflowTypeId: 'wf-1', top: PEER_CANDIDATE_PAGE_SIZE },
    })
  })

  it('omits an absent workflow type', async () => {
    getSpy.mockResolvedValue({ data: [] })

    await service.getPeerCandidates({})

    expect(getSpy).toHaveBeenCalledWith('api/v1/counterparties/peer-candidates', {
      params: { top: PEER_CANDIDATE_PAGE_SIZE },
    })
  })

  it('caps the requested page size at the size the server accepts', async () => {
    getSpy.mockResolvedValue({ data: [] })

    await service.getPeerCandidates({ top: 10000 })

    expect(getSpy).toHaveBeenCalledWith('api/v1/counterparties/peer-candidates', {
      params: { top: PEER_CANDIDATE_PAGE_SIZE },
    })
  })

  it('returns the candidates the API sends', async () => {
    getSpy.mockResolvedValue({
      data: [
        {
          id: 'c-1',
          name: 'Acme',
          nameVariants: '',
          isEligible: true,
          availability: 'Available',
        },
      ],
    })

    const result = await service.getPeerCandidates({})

    expect(result).toHaveLength(1)
    expect(result[0]!.id).toBe('c-1')
  })

  it('returns an empty list when the API sends no body', async () => {
    getSpy.mockResolvedValue({ data: null })

    await expect(service.getPeerCandidates({})).resolves.toEqual([])
  })
})
