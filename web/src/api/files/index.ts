import { useMutation, type UseMutationReturnType } from '@tanstack/vue-query'
import Service from './service'
import type { ServiceParams } from '../schema'
import type { FileDto, UploadFileMutationVariables } from './schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useUploadDocument(): UseMutationReturnType<FileDto, Error, UploadFileMutationVariables, unknown> {
    return useMutation({
      mutationFn: ({ formData, onUploadProcessChange }) =>
        this.service.uploadDocument(formData, onUploadProcessChange),
    })
  }

  useGetFile(): UseMutationReturnType<any, Error, { fileId: string; disposition?: 'preview' | 'download' }, unknown> {
    return useMutation({
      mutationFn: ({ fileId, disposition = 'download' }) =>
        this.service.getFile(fileId, disposition),
    })
  }
}
