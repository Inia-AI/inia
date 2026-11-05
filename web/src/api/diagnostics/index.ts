import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { ServiceParams } from '../schema'

export default class Log {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useDownloadLog(): UseMutationReturnType<any, Error, any, unknown> {
    return useMutation({
      mutationFn: () => this.service.downloadLog(),
      onSuccess: () => {},
    })
  }
}
