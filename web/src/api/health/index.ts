import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { ServiceParams } from '../schema'

export default class Health {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useHealth(): UseQueryReturnType<any, Error> {
    return useQuery({
      queryKey: ['health'],
      queryFn: () => this.service.getHealth(),
      retryDelay: 1000,
    })
  }
}
