import type { Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/components/ui/toast/use-toast'
import { downloadBlob, readBlobProblemDetail } from '@/lib/downloadFile'

export interface BundleExport<TArg> {
  mutateAsync: (arg: TArg) => Promise<{ blob: Blob; filename: string | null }>
  isPending: Ref<boolean>
}

// A bundle export is one server call that also names the file. The page only clicks the
// download and speaks when the server refuses, so every export page shares this one shape.
// `download` says whether the file arrived, so a dialog can close only then.
export function useBundleDownload<TArg = void>(
  exportBundle: BundleExport<TArg>,
  fallbackName: string,
  errorKey: string,
) {
  const { toast } = useToast()
  const { t: $t } = useI18n()

  const download = async (arg: TArg): Promise<boolean> => {
    try {
      const { blob, filename } = await exportBundle.mutateAsync(arg)
      downloadBlob(blob, filename ?? fallbackName)
      return true
    } catch (error) {
      toast({
        description: (await readBlobProblemDetail(error)) ?? $t(errorKey),
        variant: 'destructive',
      })
      return false
    }
  }

  return { download, isPending: exportBundle.isPending }
}
