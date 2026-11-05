import { BaseService } from '../base'
import { parseFilenameFromContentDisposition } from '@/lib/downloadFile'
import type {
  CreateReportFeedbackRequest,
  ReportExportFormat,
  ReportFeedbackItem,
  UpdateReportFeedbackStatusRequest,
  UpdateReportParagraphRequest,
  WorkflowReportSummary,
  ReportDataOutput,
  ReportSection,
  WorkflowReport,
} from './schema'
import { toOdata } from '../base/adapter'
import type {
  ServiceParams,
  OData,
  ODataQueryParams,
  ODataResponse,
  ListResponse,
} from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getWorkflowReports(
    params?: ODataQueryParams,
  ): Promise<OData<WorkflowReportSummary>> {
    const { data } = await this.instance.get<
      ODataResponse<WorkflowReportSummary>
    >(this.getApiPath('workflow-reports'), {
      params,
    })
    return toOdata(data)
  }

  async getWorkflowReport(id: string) {
    const { data } = await this.instance.get<WorkflowReport>(
      this.getApiPath(`workflow-reports/${id}`),
    )
    return data
  }

  async getWorkflowReportSections(id: string) {
    const { data } = await this.instance.get<ListResponse<ReportSection>>(
      this.getApiPath(`workflow-reports/${id}/elements`),
    )
    return data?.value ?? []
  }

  async getWorkflowReportData(id: string) {
    const { data } = await this.instance.get<ListResponse<ReportDataOutput>>(
      this.getApiPath(`workflow-reports/${id}/data`),
    )
    return data?.value ?? []
  }

  async deleteWorkflowReport(id: string) {
    const { data } =
      (await this.instance.delete(
        this.getApiPath(`workflow-reports/${id}`),
      )) || {}
    return data
  }

  async updateReportParagraph(
    id: string,
    paragraphId: string,
    params: UpdateReportParagraphRequest,
  ) {
    const { data } =
      (await this.instance.put(
        this.getApiPath(`workflow-reports/${id}/paragraphs/${paragraphId}`),
        params,
      )) || {}
    return data
  }

  async deleteReportSection(id: string, sectionId: string) {
    const { data } =
      (await this.instance.delete(
        this.getApiPath(`workflow-reports/${id}/sections/${sectionId}`),
      )) || {}
    return data
  }

  async deleteReportParagraph(id: string, paragraphId: string) {
    const { data } =
      (await this.instance.delete(
        this.getApiPath(`workflow-reports/${id}/paragraphs/${paragraphId}`),
      )) || {}
    return data
  }

  async getReportFeedback(id: string): Promise<ReportFeedbackItem[]> {
    const { data } = await this.instance.get<ListResponse<ReportFeedbackItem>>(
      this.getApiPath(`workflow-reports/${id}/feedback`),
    )
    return data?.value ?? []
  }

  async createReportFeedback(
    id: string,
    params: CreateReportFeedbackRequest,
  ): Promise<ReportFeedbackItem> {
    const { data } = await this.instance.post<ReportFeedbackItem>(
      this.getApiPath(`workflow-reports/${id}/feedback`),
      params,
    )
    return data
  }

  async updateReportFeedbackStatus(
    id: string,
    feedbackId: string,
    params: UpdateReportFeedbackStatusRequest,
  ): Promise<ReportFeedbackItem> {
    const { data } = await this.instance.patch<ReportFeedbackItem>(
      this.getApiPath(`workflow-reports/${id}/feedback/${feedbackId}/status`),
      params,
    )
    return data
  }

  async deleteReportFeedback(id: string, feedbackId: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`workflow-reports/${id}/feedback/${feedbackId}`))
  }

  async exportWorkflowReport(id: string, format: ReportExportFormat) {
    const response = await this.instance.get(
      this.getApiPath(`workflow-reports/${id}/export/${format}`),
      {
        responseType: 'blob',
      },
    )
    const rawHeader =
      response.headers?.['content-disposition'] ??
      response.headers?.['Content-Disposition']
    return {
      blob: response.data as Blob,
      filename: parseFilenameFromContentDisposition(
        typeof rawHeader === 'string' ? rawHeader : undefined,
      ),
    }
  }
}
