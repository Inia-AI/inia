import { computed, Ref } from 'vue'
import { useQuery, UseQueryReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'
import { Job } from './schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetJobs(
    params: Ref<ODataQueryParams>,
  ): UseQueryReturnType<OData<Job>, Error> {
    return useQuery({
      queryKey: computed(() => ['jobs', JSON.stringify(params.value)]),
      queryFn: () => this.service.getJobs(params.value),
    })
  }
}
