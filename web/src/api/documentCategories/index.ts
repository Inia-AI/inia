import {
  useMutation,
  type UseMutationReturnType,
  useQuery,
  useQueryClient,
} from '@tanstack/vue-query'
import Service from './service'
import type { DocumentCategory, CreateDocumentCategoryRequest, UpdateDocumentCategoryRequest } from './schema'
import type { ServiceParams } from '../schema'

export default class {
  service: Service

  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  /**
   * Hook to get all document categories
   */
  useGetDocumentCategories(config: object = {}) {
    return useQuery<DocumentCategory[]>({
      queryKey: ['documentCategories'],
      queryFn: () => this.service.getDocumentCategories(),
      ...config,
    })
  }

  /**
   * Hook to create a new document category
   */
  useCreateDocumentCategory(): UseMutationReturnType<DocumentCategory, Error, CreateDocumentCategoryRequest, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (params: CreateDocumentCategoryRequest) =>
        this.service.createDocumentCategory(params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documentCategories'] })
      },
    })
  }

  /**
   * Hook to update a document category
   */
  useUpdateDocumentCategory(): UseMutationReturnType<DocumentCategory, Error, { id: string; data: UpdateDocumentCategoryRequest }, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ id, data }: { id: string; data: UpdateDocumentCategoryRequest }) =>
        this.service.updateDocumentCategory(id, data),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documentCategories'] })
      },
    })
  }

  /**
   * Hook to delete a document category
   */
  useDeleteDocumentCategory(): UseMutationReturnType<void, Error, string, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (id: string) => this.service.deleteDocumentCategory(id),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documentCategories'] })
      },
    })
  }
}
