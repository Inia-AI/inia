import { ref, shallowRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/components/ui/toast/use-toast'
import { extractApiErrorMessage } from '@/api/base/errors'

export interface ConfirmedTarget {
  id: string
  title?: string
}

interface MutationLike {
  mutateAsync: (id: string) => Promise<unknown>
  isPending: Ref<boolean>
}

interface UseConfirmedMutationOptions<T extends ConfirmedTarget> {
  // The TanStack mutation result whose `mutateAsync(id)` carries out the
  // destructive action. The composable doesn't call any hooks itself —
  // callers compose: `useConfirmedMutation({ mutation: API.X.useY() })`.
  mutation: MutationLike
  // i18n keys used for the success and error toasts.
  successKey: string
  errorKey: string
  onSuccess?: (target: T) => void
}

/**
 * Generic "confirm-then-mutate" flow shared by Cancel-execution and
 * Delete-report. Owns the prompt/confirm/dismiss/showDialog/target state
 * and the toast-on-result side effects. Callers wire the specific mutation
 * hook and i18n strings; everything else is identical.
 */
export function useConfirmedMutation<T extends ConfirmedTarget>(
  options: UseConfirmedMutationOptions<T>,
) {
  const { mutation, successKey, errorKey, onSuccess } = options
  const { toast } = useToast()
  const { t: $t } = useI18n()

  const showDialog = ref(false)
  const target = shallowRef<T | null>(null)

  // Re-entry guard: the confirm dialog closes the moment the user clicks
  // confirm, so without this a second prompt could overwrite the in-flight
  // target before the original mutation settles — and the original's
  // `finally { dismiss }` would then clobber the second dialog.
  const prompt = (entity: T) => {
    if (mutation.isPending.value) {
      return
    }
    target.value = entity
    showDialog.value = true
  }

  const dismiss = () => {
    showDialog.value = false
    target.value = null
  }

  const confirm = async () => {
    const current = target.value
    if (!current) {
      return
    }

    try {
      await mutation.mutateAsync(current.id)
      toast({
        class: 'text-sm',
        title: $t(successKey),
        duration: 2000,
      })
      onSuccess?.(current)
    } catch (error) {
      // Surface the server-provided refusal reason (e.g. "run is still in progress")
      // under the generic title, so the user learns why the action was rejected.
      const detail = extractApiErrorMessage(error, '')
      toast({
        class: 'text-sm',
        title: $t(errorKey),
        description: detail || undefined,
        variant: 'destructive',
        duration: 3000,
      })
    } finally {
      dismiss()
    }
  }

  return {
    showDialog,
    target,
    isPending: mutation.isPending,
    prompt,
    confirm,
    dismiss,
  }
}
