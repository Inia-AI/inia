import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import PageService from './service'
import type { ServiceParams } from '../schema'
import type { PageImageResponse } from './schema'

export default class PageApi {
  service: PageService

  constructor(params?: ServiceParams) {
    this.service = new PageService(params)
  }

  useGetPageImage(): UseMutationReturnType<PageImageResponse, Error, string, unknown> {
    return useMutation({
      mutationFn: (pageId: string) => this.service.getPageImage(pageId),
    })
  }
}
