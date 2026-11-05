import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import {
  PEER_CANDIDATE_PAGE_SIZE,
  type PeerCandidate,
  type PeerCandidateQueryParams,
} from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getPeerCandidates(
    params: PeerCandidateQueryParams,
  ): Promise<PeerCandidate[]> {
    const search = params.search?.trim()

    const { data } = await this.instance.get<PeerCandidate[]>(
      this.getApiPath('counterparties/peer-candidates'),
      {
        params: {
          ...(params.workflowTypeId
            ? { workflowTypeId: params.workflowTypeId }
            : {}),
          ...(search ? { search } : {}),
          top: Math.min(
            params.top || PEER_CANDIDATE_PAGE_SIZE,
            PEER_CANDIDATE_PAGE_SIZE,
          ),
        },
      },
    )

    return data ?? []
  }
}
