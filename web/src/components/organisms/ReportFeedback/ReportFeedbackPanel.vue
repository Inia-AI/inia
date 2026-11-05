<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import {
  ReportFeedbackCategory,
  ReportFeedbackStatus,
  type ReportFeedbackItem,
} from '@/api/workflowReports/schema'
import { extractApiErrorMessage } from '@/api/base/errors'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { useToast } from '@/components/ui/toast/use-toast'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { formatDateTime } from '@/lib/utils'

const props = defineProps<{
  executionId: string
  /** The reader's selection in the report body when the panel was opened; empty for none. */
  quotedText?: string
}>()
const open = defineModel<boolean>('open', { required: true })

const { t: $t } = useI18n()
const { toast } = useToast()
const { isEditor } = useCurrentUserRoles()

const KEY = 'page.workflowDetails.feedback'

// Column widths of ReportFeedbackItem on the platform; the API refuses longer values.
const MAX_QUOTED_TEXT_LENGTH = 4000
const MAX_COMMENT_LENGTH = 4000
const MAX_DISMISS_REASON_LENGTH = 500

const { data: feedback, isError: loadFailed } = API.WorkflowReports.useGetReportFeedback(
  props.executionId,
)
const createFeedback = API.WorkflowReports.useCreateReportFeedback()
const updateStatus = API.WorkflowReports.useUpdateReportFeedbackStatus()
const deleteFeedback = API.WorkflowReports.useDeleteReportFeedback()

const items = computed<ReportFeedbackItem[]>(() => feedback.value ?? [])

const categories = Object.values(ReportFeedbackCategory)
const statuses = Object.values(ReportFeedbackStatus)

const category = ref<ReportFeedbackCategory | ''>('')
const comment = ref('')

const incomplete = computed(
  () => category.value === '' || comment.value.trim().length === 0,
)

const reset = () => {
  category.value = ''
  comment.value = ''
}

// Dismissing needs a reason, so it is a two-step change on the one item being dismissed.
const dismissingId = ref<string | null>(null)
const dismissReason = ref('')

// Bumped when a status change fails, so the native select re-renders back to the stored status.
const statusRevision = ref(0)

const submit = async () => {
  if (incomplete.value || createFeedback.isPending.value) {
    return
  }
  const quoted = (props.quotedText?.trim() ?? '').slice(0, MAX_QUOTED_TEXT_LENGTH)
  try {
    await createFeedback.mutateAsync({
      id: props.executionId,
      params: {
        quotedText: quoted.length > 0 ? quoted : null,
        category: category.value as ReportFeedbackCategory,
        comment: comment.value.trim(),
      },
    })
    reset()
    toast({ class: 'text-sm', description: $t(`${KEY}.submitted`) })
  } catch (error) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: extractApiErrorMessage(error, $t(`${KEY}.submitFailed`)),
    })
  }
}

const applyStatus = async (
  feedbackId: string,
  status: ReportFeedbackStatus,
  reason: string | null,
) => {
  try {
    await updateStatus.mutateAsync({
      id: props.executionId,
      feedbackId,
      params: { status, dismissReason: reason },
    })
    toast({ class: 'text-sm', description: $t(`${KEY}.statusUpdated`) })
  } catch (error) {
    statusRevision.value++
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: extractApiErrorMessage(error, $t(`${KEY}.statusFailed`)),
    })
  }
}

const onStatusChange = async (item: ReportFeedbackItem, event: Event) => {
  const status = (event.target as HTMLSelectElement).value as ReportFeedbackStatus
  if (status === ReportFeedbackStatus.Dismissed) {
    dismissingId.value = item.id
    dismissReason.value = ''
    return
  }
  dismissingId.value = null
  if (status !== item.status) {
    await applyStatus(item.id, status, null)
  }
}

const cancelDismiss = () => {
  dismissingId.value = null
  dismissReason.value = ''
}

const confirmDismiss = async () => {
  const id = dismissingId.value
  const reason = dismissReason.value.trim()
  if (!id || reason.length === 0) {
    return
  }
  await applyStatus(id, ReportFeedbackStatus.Dismissed, reason)
  cancelDismiss()
}

const deletingId = ref<string | null>(null)
const confirmingDelete = computed({
  get: () => deletingId.value !== null,
  set: (value: boolean) => {
    if (!value) {
      deletingId.value = null
    }
  },
})

const confirmDelete = async () => {
  const feedbackId = deletingId.value
  if (!feedbackId) {
    return
  }
  try {
    await deleteFeedback.mutateAsync({ id: props.executionId, feedbackId })
    if (dismissingId.value === feedbackId) {
      cancelDismiss()
    }
    toast({ class: 'text-sm', description: $t(`${KEY}.deleted`) })
  } catch (error) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: extractApiErrorMessage(error, $t(`${KEY}.deleteFailed`)),
    })
  }
}

const statusValue = (item: ReportFeedbackItem) =>
  dismissingId.value === item.id ? ReportFeedbackStatus.Dismissed : item.status


const titleId = useId()
const title = ref<HTMLHeadingElement | null>(null)

defineExpose({ focus: (options?: FocusOptions) => title.value?.focus(options) })
</script>

<template>
  <section v-if="open" class="report-feedback" data-testid="report-feedback" :aria-labelledby="titleId">
    <div class="feedback-header">
      <h2 :id="titleId" ref="title" class="feedback-title" tabindex="-1">{{ $t(`${KEY}.title`) }}</h2>
      <Button variant="ghost" size="sm" @click="open = false">{{ $t(`${KEY}.close`) }}</Button>
    </div>

    <form class="feedback-form" @submit.prevent="submit">
      <div v-if="quotedText" class="form-field">
        <span class="hint">{{ $t(`${KEY}.quoted`) }}</span>
        <blockquote class="quoted" data-testid="feedback-quoted">{{ quotedText }}</blockquote>
      </div>
      <label class="form-field">
        <span class="hint">{{ $t(`${KEY}.category`) }}</span>
        <select v-model="category" class="native-select" data-testid="feedback-category">
          <option value="" disabled>{{ $t(`${KEY}.categoryPlaceholder`) }}</option>
          <option v-for="option in categories" :key="option" :value="option">
            {{ $t(`${KEY}.categories.${option}`) }}
          </option>
        </select>
      </label>
      <label class="form-field">
        <span class="hint">{{ $t(`${KEY}.comment`) }}</span>
        <Textarea
          v-model="comment"
          rows="3"
          :maxlength="MAX_COMMENT_LENGTH"
          :placeholder="$t(`${KEY}.commentPlaceholder`)"
          data-testid="feedback-comment"
        />
      </label>
      <div class="form-actions">
        <Button
          type="submit"
          size="sm"
          :disabled="incomplete || createFeedback.isPending.value"
          data-testid="feedback-submit"
        >
          {{ $t(`${KEY}.submit`) }}
        </Button>
      </div>
    </form>

    <ul v-if="items.length > 0" class="feedback-items">
      <li
        v-for="item in items"
        :key="item.id"
        class="feedback-item"
        :data-testid="`feedback-item-${item.id}`"
      >
        <div class="feedback-item-head">
          <span class="badge">{{ $t(`${KEY}.categories.${item.category}`) }}</span>
          <div class="feedback-item-actions">
            <span class="status" :class="`status-${item.status.toLowerCase()}`">
              {{ $t(`${KEY}.statuses.${item.status}`) }}
            </span>
            <Button
              variant="ghost"
              size="sm"
              :disabled="deleteFeedback.isPending.value"
              :data-testid="`feedback-delete-${item.id}`"
              @click="deletingId = item.id"
            >
              {{ $t(`${KEY}.delete`) }}
            </Button>
          </div>
        </div>
        <blockquote v-if="item.quotedText" class="quoted">{{ item.quotedText }}</blockquote>
        <p class="comment">{{ item.comment }}</p>
        <p class="meta">
          <span>{{ item.authorDisplayName }}</span>
          <span> · {{ formatDateTime(item.createdAt) }}</span>
        </p>
        <p
          v-if="item.status === ReportFeedbackStatus.Dismissed && item.dismissReason"
          class="hint"
        >
          {{ item.dismissReason }}
        </p>

        <div v-if="isEditor" class="status-control">
          <label class="hint">
            {{ $t(`${KEY}.status`) }}
            <select
              :key="`${item.id}-${item.status}-${statusRevision}`"
              class="native-select"
              :value="statusValue(item)"
              :disabled="updateStatus.isPending.value"
              :data-testid="`feedback-status-${item.id}`"
              @change="onStatusChange(item, $event)"
            >
              <option v-for="status in statuses" :key="status" :value="status">
                {{ $t(`${KEY}.statuses.${status}`) }}
              </option>
            </select>
          </label>
          <div v-if="dismissingId === item.id" class="dismiss">
            <Input
              v-model="dismissReason"
              :maxlength="MAX_DISMISS_REASON_LENGTH"
              :placeholder="$t(`${KEY}.dismissReasonPlaceholder`)"
              data-testid="feedback-dismiss-reason"
            />
            <Button
              size="sm"
              :disabled="dismissReason.trim().length === 0 || updateStatus.isPending.value"
              data-testid="feedback-dismiss-confirm"
              @click="confirmDismiss"
            >
              {{ $t(`${KEY}.confirmDismiss`) }}
            </Button>
            <Button variant="ghost" size="sm" @click="cancelDismiss">
              {{ $t(`${KEY}.cancelDismiss`) }}
            </Button>
          </div>
        </div>
      </li>
    </ul>
    <p v-else-if="loadFailed" class="hint">{{ $t(`${KEY}.loadFailed`) }}</p>
    <p v-else class="hint">{{ $t(`${KEY}.empty`) }}</p>

    <ConfirmDialog
      v-model="confirmingDelete"
      :title="$t(`${KEY}.deleteTitle`)"
      :description="$t(`${KEY}.deleteMessage`)"
      :confirm-label="$t(`${KEY}.deleteConfirm`)"
      :cancel-label="$t(`${KEY}.deleteCancel`)"
      @confirm="confirmDelete"
    />
  </section>
</template>

<style scoped>
.report-feedback {
  margin-bottom: var(--spacing-lg, 24px);
  padding: var(--spacing-md);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-small, 6px);
  background: var(--bg-2);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.feedback-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.feedback-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.feedback-items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.feedback-item {
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-small, 6px);
  background: var(--bg-1, transparent);
  border: 1px solid var(--stroke);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.feedback-item-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
}

.feedback-item-actions {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.badge {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-secondary);
}

.status {
  font-size: 11px;
  font-weight: 600;
  padding: 1px 8px;
  border-radius: 999px;
  border: 1px solid var(--stroke);
  color: var(--text-secondary);
}

.status-addressed {
  color: var(--primary);
  border-color: var(--primary);
}

.status-dismissed {
  text-decoration: line-through;
}

.quoted {
  margin: 0;
  padding-left: var(--spacing-sm);
  border-left: 3px solid var(--primary);
  font-size: 13px;
  color: var(--text-secondary);
  font-style: italic;
  white-space: pre-wrap;
}

.comment {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
  white-space: pre-wrap;
}

.meta,
.hint {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
}

.status-control {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  margin-top: 4px;
}

.status-control label {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.dismiss {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.feedback-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-actions {
  display: flex;
  justify-content: flex-end;
}

.native-select {
  height: 32px;
  padding: 0 var(--spacing-sm);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-small, 6px);
  background: var(--bg-1, transparent);
  color: var(--text-primary);
  font-size: 13px;
}
</style>
