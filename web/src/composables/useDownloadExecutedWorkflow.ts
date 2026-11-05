import { useI18n } from 'vue-i18n'
import API from '@/api'
import { useToast } from '@/components/ui/toast/use-toast'
import { downloadBlob, readBlobProblemDetail } from '@/lib/downloadFile'
import type { DownloadExecutedWorkflowParams } from '@/api/executedWorkflows/schema'

export function useDownloadExecutedWorkflow() {
  const mutation = API.ExecutedWorkflows.useDownloadExecutedWorkflow()
  const { toast } = useToast()
  const { t: $t } = useI18n()

  async function download(params: DownloadExecutedWorkflowParams) {
    try {
      const { blob, filename } = await mutation.mutateAsync(params)
      downloadBlob(blob, filename)
      toast({
        title: $t('shared.workflowDownload.downloadSuccess'),
        duration: 2000,
      })
    } catch (err) {
      toast({
        title: $t('shared.workflowDownload.downloadError'),
        description: await readBlobProblemDetail(err),
        variant: 'destructive',
        duration: 3000,
      })
    }
  }

  return {
    download,
    isPending: mutation.isPending,
  }
}
