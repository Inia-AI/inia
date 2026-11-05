import { computed, Ref } from 'vue'
import {
  useMutation,
  UseMutationReturnType,
  useQuery,
  useQueryClient,
} from '@tanstack/vue-query'
import Service from './service'
import type {
  CreateReportFeedbackRequest,
  ReportExportFormat,
  ReportFeedbackItem,
  UpdateReportFeedbackStatusRequest,
  UpdateReportParagraphRequest,
} from './schema'
import type { ServiceParams, ODataQueryParams } from '../schema'

export const WORKFLOW_REPORT_QUERY_KEYS = {
  reports: 'workflow-reports',
  sections: 'workflow-report-sections',
  dataOutputs: 'workflow-report-data-outputs',
  feedback: 'workflow-report-feedback',
} as const

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetWorkflowReports(
    params: Ref<ODataQueryParams> | null = null,
    configs = {},
  ) {
    return useQuery({
      queryKey: computed(() => [
        WORKFLOW_REPORT_QUERY_KEYS.reports,
        JSON.stringify(params?.value),
      ]),
      queryFn: async () => {
        return this.service.getWorkflowReports(params?.value)
      },
      placeholderData: prev => prev,
      ...configs,
    })
  }

  useGetWorkflowReportById(id: string, configs = {}) {
    return useQuery({
      queryKey: [WORKFLOW_REPORT_QUERY_KEYS.reports, id],
      queryFn: async () => {
        return this.service.getWorkflowReport(id)
      },
      ...configs,
    })
  }

  useGetWorkflowReportSections(id: string) {
    return useQuery({
      queryKey: [WORKFLOW_REPORT_QUERY_KEYS.sections, id],
      queryFn: async () => {
        return this.service.getWorkflowReportSections(id)
      },
    })
  }

  useGetWorkflowReportData(id: string) {
    return useQuery({
      queryKey: [WORKFLOW_REPORT_QUERY_KEYS.dataOutputs, id],
      queryFn: async () => {
        return this.service.getWorkflowReportData(id)
      },
    })
  }

  useUpdateReportParagraph(): UseMutationReturnType<any, Error, any, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({
        id,
        paragraphId,
        params,
      }: {
        id: string
        paragraphId: string
        params: UpdateReportParagraphRequest
      }) => this.service.updateReportParagraph(id, paragraphId, params),
      onSuccess: () => {
        queryClient.invalidateQueries()
      },
    })
  }

  useDeleteWorkflowReport() {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (id: string) => this.service.deleteWorkflowReport(id),
      onSuccess: () => {
        queryClient.invalidateQueries()
      },
    })
  }

  useDeleteReportParagraph(): UseMutationReturnType<any, Error, any, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ id, paragraphId }: { id: string; paragraphId: string }) =>
        this.service.deleteReportParagraph(id, paragraphId),
      onSuccess: () => {
        queryClient.invalidateQueries()
      },
    })
  }

  useGetReportFeedback(id: string) {
    return useQuery({
      queryKey: [WORKFLOW_REPORT_QUERY_KEYS.feedback, id],
      queryFn: async () => {
        return this.service.getReportFeedback(id)
      },
    })
  }

  useCreateReportFeedback(): UseMutationReturnType<
    ReportFeedbackItem,
    Error,
    { id: string; params: CreateReportFeedbackRequest },
    unknown
  > {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ id, params }: { id: string; params: CreateReportFeedbackRequest }) =>
        this.service.createReportFeedback(id, params),
      onSuccess: (_item, { id }) => {
        queryClient.invalidateQueries({
          queryKey: [WORKFLOW_REPORT_QUERY_KEYS.feedback, id],
        })
      },
    })
  }

  useUpdateReportFeedbackStatus(): UseMutationReturnType<
    ReportFeedbackItem,
    Error,
    { id: string; feedbackId: string; params: UpdateReportFeedbackStatusRequest },
    unknown
  > {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({
        id,
        feedbackId,
        params,
      }: {
        id: string
        feedbackId: string
        params: UpdateReportFeedbackStatusRequest
      }) => this.service.updateReportFeedbackStatus(id, feedbackId, params),
      onSuccess: (_item, { id }) => {
        queryClient.invalidateQueries({
          queryKey: [WORKFLOW_REPORT_QUERY_KEYS.feedback, id],
        })
      },
    })
  }

  useDeleteReportFeedback(): UseMutationReturnType<
    void,
    Error,
    { id: string; feedbackId: string },
    unknown
  > {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ id, feedbackId }: { id: string; feedbackId: string }) =>
        this.service.deleteReportFeedback(id, feedbackId),
      onSuccess: (_result, { id, feedbackId }) => {
        queryClient.setQueryData<ReportFeedbackItem[]>(
          [WORKFLOW_REPORT_QUERY_KEYS.feedback, id],
          items => items?.filter(item => item.id !== feedbackId),
        )
        queryClient.invalidateQueries({
          queryKey: [WORKFLOW_REPORT_QUERY_KEYS.feedback, id],
        })
      },
    })
  }

  useExportWorkflowReport(id: string) {
    return useMutation({
      mutationFn: (format: ReportExportFormat) => this.service.exportWorkflowReport(id, format),
    })
  }
}
