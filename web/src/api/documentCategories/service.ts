import { BaseService } from '../base'
import type { DocumentCategory, CreateDocumentCategoryRequest, UpdateDocumentCategoryRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Get all document categories
   */
  async getDocumentCategories(): Promise<DocumentCategory[]> {
    try {
      const { data } = await this.instance.get<DocumentCategory[]>(
        this.getApiPath('document-categories')
      )
      return data
    } catch {
      return []
    }
  }

  /**
   * Create a new document category
   */
  async createDocumentCategory(params: CreateDocumentCategoryRequest): Promise<DocumentCategory> {
    const { data } = await this.instance.post<DocumentCategory>(
      this.getApiPath('document-categories'),
      params
    )
    return data
  }

  /**
   * Update an existing document category
   */
  async updateDocumentCategory(id: string, params: UpdateDocumentCategoryRequest): Promise<DocumentCategory> {
    const { data } = await this.instance.put<DocumentCategory>(
      this.getApiPath(`document-categories/${id}`),
      params
    )
    return data
  }

  /**
   * Delete a document category
   */
  async deleteDocumentCategory(id: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`document-categories/${id}`))
  }
}
