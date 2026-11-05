import { computed, type Ref } from 'vue'
import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryOptions,
} from '@tanstack/vue-query'
import Service from './service'
import {
  isDocumentProcessing,
  type BulkDeleteResult,
  type DocumentDetail,
  type DocumentSummary,
  type DocumentPagesQueryParams,
  type DocumentsQueryParams,
  type PageDetail,
  type PageSummary,
  type UpdateDocumentRequest,
} from './schema'
import type { OData, ServiceParams } from '../schema'

const DOCUMENT_PROCESSING_POLL_MS = 2500

export default class {
  service: Service

  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetDocuments(
    params: Ref<DocumentsQueryParams>,
    config: Record<string, unknown> = {},
  ) {
    return useQuery<OData<DocumentSummary> | null>({
      queryKey: computed(() => ['documents', 'list', JSON.stringify(params.value)]),
      queryFn: () => this.service.getDocuments(params.value),
      ...config,
    })
  }

  useGetDocumentById(id: Ref<string>, config: Partial<UseQueryOptions<DocumentDetail>> = {}) {
    return useQuery<DocumentDetail>({
      queryKey: computed(() => ['documents', 'detail', id.value]),
      queryFn: () => this.service.getDocument(id.value),
      enabled: computed(() => Boolean(id.value)),
      ...config,
    })
  }

  /** Reads a document and polls it while the pipeline is still reading it. */
  useWatchDocumentProcessing(id: Ref<string>) {
    return this.useGetDocumentById(id, {
      refetchInterval: query =>
        isDocumentProcessing(query.state.data?.processingState) ? DOCUMENT_PROCESSING_POLL_MS : false,
    })
  }

  useGetDocumentPages(docId: Ref<string>, params: Ref<DocumentPagesQueryParams>) {
    return useQuery<OData<PageSummary> | null>({
      queryKey: computed(() => [
        'documents',
        'pages',
        docId.value,
        JSON.stringify(params.value),
      ]),
      queryFn: () => this.service.getDocumentPages(docId.value, params.value),
      enabled: computed(() => Boolean(docId.value)),
    })
  }

  useGetDocumentPage(docId: Ref<string>, pageNumber: Ref<number | null>) {
    return useQuery<PageDetail>({
      queryKey: computed(() => ['documents', 'page', docId.value, pageNumber.value]),
      queryFn: () => this.service.getDocumentPage(docId.value, pageNumber.value as number),
      enabled: computed(() => Boolean(docId.value) && pageNumber.value !== null),
    })
  }

  useUpdateDocument(): UseMutationReturnType<
    DocumentDetail,
    Error,
    { id: string; data: UpdateDocumentRequest },
    unknown
  > {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: ({ id, data }: { id: string; data: UpdateDocumentRequest }) =>
        this.service.updateDocument(id, data),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documents'] })
      },
    })
  }

  useDownloadDocumentText() {
    return useMutation({
      mutationFn: (id: string) => this.service.downloadDocumentText(id),
    })
  }

  useProcessDocument(): UseMutationReturnType<DocumentSummary, Error, string, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (id: string) => this.service.processDocument(id),
      onSuccess: (queued, id) => {
        queryClient.setQueryData<DocumentDetail>(['documents', 'detail', id], cached =>
          cached && { ...cached, processingState: queued.processingState, processingError: queued.processingError },
        )
        queryClient.invalidateQueries({ queryKey: ['documents', 'detail', id] })
      },
    })
  }

  useDeleteDocument(): UseMutationReturnType<void, Error, string, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (id: string) => this.service.deleteDocument(id),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documents'] })
      },
    })
  }

  useBulkDeleteDocuments(): UseMutationReturnType<
    BulkDeleteResult,
    Error,
    string[],
    unknown
  > {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (ids: string[]) => this.service.bulkDelete(ids),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['documents'] })
      },
    })
  }
}
