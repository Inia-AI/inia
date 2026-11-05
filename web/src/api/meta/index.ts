import { useQuery } from '@tanstack/vue-query'
import Service from './service'
import type { ServiceParams } from '../schema'

export default class Version {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetVersion() {
    return useQuery({
      queryKey: ['version'],
      queryFn: async () => {
        return this.service.getVersion()
      },
    })
  }
}
