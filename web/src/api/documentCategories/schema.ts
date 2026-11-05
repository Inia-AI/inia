import { z } from 'zod'

/**
 * Document Category from API
 */
export interface DocumentCategory {
  id: string
  name: string
  displayOrder: number
  createdAt: string
}

/**
 * Request to create a new document category
 */
export interface CreateDocumentCategoryRequest {
  name: string
  displayOrder?: number
}

/**
 * Request to update an existing document category
 */
export interface UpdateDocumentCategoryRequest {
  name?: string
  displayOrder?: number
}

/**
 * Zod schema for document category validation
 */
export const documentCategorySchema = z.object({
  id: z.string().min(1),
  name: z.string().min(1, 'Name is required').max(200),
  displayOrder: z.number().int().min(0),
  createdAt: z.string()
})

/**
 * Zod schema for create document category request validation
 */
export const createDocumentCategorySchema = z.object({
  name: z.string().min(1, 'Name is required').max(200),
  displayOrder: z.number().int().min(0).optional()
})

/**
 * Zod schema for update document category request validation
 */
export const updateDocumentCategorySchema = z.object({
  name: z.string().max(200).optional(),
  displayOrder: z.number().int().min(0).optional()
})
