import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type { FileDto } from './schema'

const FILES_PATH = 'files'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  uploadEndpoint(pageOrigin: string): string {
    const base = this.instance.defaults.baseURL || '/'
    const absoluteBase = new URL(base.endsWith('/') ? base : `${base}/`, pageOrigin)
    return new URL(this.getApiPath(FILES_PATH), absoluteBase).toString()
  }

  async uploadDocument(
    formData: FormData,
    onUploadProcessChange: (percent: number) => void,
  ): Promise<FileDto> {
    const { data } = await this.instance.post<FileDto>(
      this.getApiPath(FILES_PATH),
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
        onUploadProgress: progressEvent => {
          if (
            progressEvent.lengthComputable &&
            progressEvent?.total &&
            progressEvent.loaded
          ) {
            onUploadProcessChange(
              Math.round((progressEvent.loaded / progressEvent.total) * 100),
            )
          }
        },
      },
    )
    return data
  }

  async getFile(fileId: string, disposition: 'preview' | 'download' = 'download') {
    const response = await this.instance.get(
      this.getApiPath(`${FILES_PATH}/${fileId}`),
      {
        params: { disposition },
        responseType: 'blob'
      }
    )
    return response
  }
}
