import { computed, type Ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import Service from './service'
import type { PeerCandidate, PeerCandidateQueryParams } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetPeerCandidates(
    params: Ref<PeerCandidateQueryParams>,
    options?: { enabled?: Ref<boolean> | boolean },
  ) {
    return useQuery<PeerCandidate[]>({
      queryKey: computed(() => [
        'peer-candidates',
        JSON.stringify(params.value),
      ]),
      queryFn: () => this.service.getPeerCandidates(params.value),
      placeholderData: previous => previous,
      enabled: options?.enabled,
    })
  }
}
