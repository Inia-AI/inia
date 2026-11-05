import { BaseService } from '../base'
import { toOdata } from '../base/adapter'
import { parseFilenameFromContentDisposition } from '@/lib/downloadFile'
import type { ODataResponse, OData, ServiceParams } from '../schema'
import type {
  BulkDeleteResult,
  DocumentDetail,
  DocumentPagesQueryParams,
  DocumentSummary,
  DocumentTextFile,
  DocumentsQueryParams,
  PageDetail,
  PageSummary,
  UpdateDocumentRequest,
} from './schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getDocuments(
    params: DocumentsQueryParams,
  ): Promise<OData<DocumentSummary> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<DocumentSummary>>(
        this.getApiPath('documents'),
        { params },
      )
      return toOdata(data)
    } catch {
      return null
    }
  }

  async getDocument(id: string): Promise<DocumentDetail> {
    const { data } = await this.instance.get<DocumentDetail>(
      this.getApiPath(`documents/${id}`),
    )
    return data
  }

  // Queues standalone processing (pages + text); poll getDocument for processingState.
  async processDocument(id: string): Promise<DocumentSummary> {
    const { data } = await this.instance.post<DocumentSummary>(
      this.getApiPath(`documents/${id}/process`),
    )
    return data
  }

  async updateDocument(id: string, request: UpdateDocumentRequest): Promise<DocumentDetail> {
    const { data } = await this.instance.patch<DocumentDetail>(
      this.getApiPath(`documents/${id}`),
      request,
    )
    return data
  }

  async getDocumentPage(id: string, pageNumber: number): Promise<PageDetail> {
    const { data } = await this.instance.get<PageDetail>(
      this.getApiPath(`documents/${id}/pages/${pageNumber}`),
    )
    return data
  }

  async downloadDocumentText(id: string): Promise<DocumentTextFile> {
    const response = await this.instance.get<Blob>(this.getApiPath(`documents/${id}/text`), {
      responseType: 'blob',
    })
    return {
      blob: response.data,
      filename: parseFilenameFromContentDisposition(response.headers['content-disposition']),
    }
  }

  async deleteDocument(id: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`documents/${id}`))
  }

  async bulkDelete(ids: string[]): Promise<BulkDeleteResult> {
    const { data } = await this.instance.post<BulkDeleteResult>(
      this.getApiPath('documents/bulk-delete'),
      { ids },
    )
    return data
  }

  async getDocumentPages(
    docId: string,
    params: DocumentPagesQueryParams,
  ): Promise<OData<PageSummary> | null> {
    try {
      const { data } = await this.instance.get<ODataResponse<PageSummary>>(
        this.getApiPath(`documents/${docId}/pages`),
        { params },
      )
      return toOdata(data)
    } catch {
      return null
    }
  }
}
