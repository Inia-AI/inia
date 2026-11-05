import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import { parseFilenameFromContentDisposition } from '@/lib/downloadFile'
import type {
  DownloadExecutedWorkflowParams,
  DownloadExecutedWorkflowResult,
} from './schema'

const DOWNLOAD_PATH = 'executed-workflows/download'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async downloadExecutedWorkflow(
    params: DownloadExecutedWorkflowParams,
  ): Promise<DownloadExecutedWorkflowResult> {
    const supplied = [
      { key: 'executionId' as const, value: params.executionId },
      { key: 'workflowId' as const, value: params.workflowId },
      { key: 'jobId' as const, value: params.jobId },
    ].filter(e => typeof e.value === 'string' && e.value.length > 0) as Array<{
      key: 'executionId' | 'workflowId' | 'jobId'
      value: string
    }>

    if (supplied.length !== 1) {
      throw new Error(
        'downloadExecutedWorkflow requires exactly one of executionId, workflowId, or jobId',
      )
    }

    const only = supplied[0]!
    const queryParams = { [only.key]: only.value } as DownloadExecutedWorkflowParams

    const response = await this.instance.get(this.getApiPath(DOWNLOAD_PATH), {
      params: queryParams,
      responseType: 'blob',
    })

    const rawHeader =
      response.headers?.['content-disposition'] ??
      response.headers?.['Content-Disposition']
    const header = typeof rawHeader === 'string' ? rawHeader : undefined

    const filename =
      parseFilenameFromContentDisposition(header) ??
      `executed-workflow-${only.value}.workflow`

    return { blob: response.data as Blob, filename }
  }
}
