import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type { PageImageResponse } from './schema'

// UUID v4 format validation regex
const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export default class PageService extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getPageImage(pageId: string): Promise<PageImageResponse> {
    // Validate pageId is a valid UUID to prevent path traversal or injection
    if (!UUID_REGEX.test(pageId)) {
      throw new Error('Invalid page ID format')
    }

    const response = await this.instance.get(this.getApiPath(`pages/${pageId}/image`), {
      responseType: 'blob',
    })

    const contentType = response.headers['content-type']
    return {
      blob: response.data,
      contentType:
        typeof contentType === 'string' && contentType !== ''
          ? contentType
          : 'application/octet-stream',
    }
  }
}
