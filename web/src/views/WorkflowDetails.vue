<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from 'vue'
import { fullFileName } from '@/lib/fileName'
import { WorkflowElement } from '@/components/organisms'
import { WorkflowCredits, FilePreviewDialog } from '@/components/molecules'
import API from '@/api'
import { refusalDetail } from '@/api/base/errors'
import { RouterLink, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import { WorkflowState } from '@/enums'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useToast } from '@/components/ui/toast/use-toast'
import {
  CancelExecutionDialog,
  DeleteReportDialog,
  RunCostDialog,
  ReplayProvenanceBadge,
  ReRunChoiceDialog,
  RunFailureNotice,
  ModelRefusalNotice,
  WorkflowActivityLine,
  WorkflowStatusPill,
} from '@/components/molecules'
import { useCancelExecution, isCancellableState } from '@/composables/useCancelExecution'
import { useDeleteReport } from '@/composables/useDeleteReport'
import { useBundleDownload } from '@/composables/useBundleDownload'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { extensionPoints } from '@/modules/extensionPoints'
import { useDownloadExecutedWorkflow } from '@/composables/useDownloadExecutedWorkflow'
import { buildChangeDocumentsPrefill, buildReRunPrefill } from '@/lib/workflowReRunPrefill'
import {
  isVariableRefusal,
  loadReRunInputs,
  planEdit,
  planRestart,
  reRunFailureMessage,
  reRunStartedMessage,
  variableRefusalPrefill,
} from '@/lib/workflowReRunPlan'
import useCreateWorkflowPrefillStore from '@/store/createWorkflowPrefill'
import { inFlightRefetchInterval } from '@/lib/workflowRefetch'
import { activityViewOf } from '@/lib/workflowActivity'
import { gridContainerClass, gridItemClass } from '@/lib/reportLayout'
import { reportTitle } from '@/lib/reportTitle'
import type { ReportExportFormat, ReportSection, WorkflowExecutionDocument } from '@/api/workflowReports/schema'
import ReportFeedbackPanel from '@/components/organisms/ReportFeedback/ReportFeedbackPanel.vue'
import ReportDataOutput from '@/components/organisms/ReportDataOutput/ReportDataOutput.vue'
import { focusFirstConnected } from '@/utils/focus'
import { OPEN_REPORT_FEEDBACK_KEY } from '@/components/organisms/ReportFeedback/injectionKeys'
import { useCanReviewSignatures, useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'
import { useItemTitle } from '@/composables/useDocumentTitle'

const router = useRouter()
const { t: $t } = useI18n()
const { toast } = useToast()

const id = router?.currentRoute?.value?.params?.id?.toString()

if (!id) {
  throw new Error('Workflow ID is required')
}

const {
  data: outputData,
  isLoading: isLoadingOutput,
  isError: isErrorOutput,
  error: outputError,
  refetch: refetchOutput,
} = API.WorkflowReports.useGetWorkflowReportSections(id)
const { data: dataOutputData, refetch: refetchDataOutputs } =
  API.WorkflowReports.useGetWorkflowReportData(id)
const { data: executionData, isLoading: isLoadingExecution, isError: isErrorExecution } =
  API.WorkflowReports.useGetWorkflowReportById(id, {
    refetchInterval: inFlightRefetchInterval(),
  })

const has = useCapabilities()
const canReadLogs = computed(() => has(CAPABILITIES.RUN_LOGS))
const canReviewSignatures = useCanReviewSignatures()
const { data: executionLogs, refetch: refetchExecutionLogs } =
  API.WorkflowExecutions.useGetWorkflowExecutionLogs(id, canReadLogs)
watch(
  () => executionData.value?.workflowState,
  (state, previous) => {
    if (state && previous && state !== previous) {
      void refetchOutput()
      if (canReadLogs.value) {
        void refetchExecutionLogs()
      }
      void refetchDataOutputs()
    }
  },
)

const { mutateAsync: updateReportParagraph } =
  API.WorkflowReports.useUpdateReportParagraph()

const { mutateAsync: deleteReportParagraph } =
  API.WorkflowReports.useDeleteReportParagraph()

const { mutateAsync: getFile } = API.Files.useGetFile()

const { mutateAsync: createWorkflow, isPending: isStartPending } =
  API.WorkflowExecutions.useCreateWorkflowExecution()
const isCheckingReRun = ref(false)
const isReRunning = computed(() => isStartPending.value || isCheckingReRun.value)

// Cancel a running execution — composable owns the API call, the toast,
// and the dialog state. We pass `onSuccess` so the user gets routed off the
// now-dead detail page instead of staring at a workflow that will never
// finish loading.
const cancelExecution = useCancelExecution({
  onSuccess: () => router.push({ name: 'Home' }),
})

// Delete a settled report — same backend endpoint as cancel, but the user
// is removing a report they're done with rather than stopping work in
// progress. After delete, route to the reports list (the detail page would
// 404).
const deleteReport = useDeleteReport({
  onSuccess: () => router.push({ name: 'WorkflowReports' }),
})

const canCancel = computed(() =>
  isCancellableState(executionData.value?.workflowState),
)

// Report-level feedback. The panel quotes the reader's text selection inside the report body
// as it stood when the Actions menu opened: the menu trigger keeps the selection alive, and
// choosing the item may collapse it, so it is snapshotted at open and re-read live as a
// fallback. A selection the reader cleared before opening the menu is never quoted.
const isFeedbackOpen = ref(false)
const isRunCostOpen = ref(false)
const feedbackQuotedText = ref('')
const reportBodyRef = ref<HTMLElement | null>(null)
const menuOpenSelection = ref('')

const selectionInReportBody = (): string => {
  const selection = window.getSelection()
  const value = selection?.toString().trim() ?? ''
  const anchorNode = selection?.anchorNode
  const body = reportBodyRef.value
  const isInBody =
    !!body &&
    !!anchorNode &&
    (body === anchorNode ||
      body.contains(anchorNode) ||
      (!!anchorNode.parentElement && body.contains(anchorNode.parentElement)))

  return isInBody ? value : ''
}

const onActionsMenuOpenChange = (open: boolean) => {
  if (open) {
    menuOpenSelection.value = selectionInReportBody()
  }
}

const feedbackPanel = ref<InstanceType<typeof ReportFeedbackPanel> | null>(null)
const actionsTrigger = ref<InstanceType<typeof Button> | null>(null)
let isFeedbackChosenFromMenu = false

const onOpenFeedback = () => {
  const live = selectionInReportBody()
  feedbackQuotedText.value = live.length > 0 ? live : menuOpenSelection.value
  menuOpenSelection.value = ''
  isFeedbackOpen.value = true
  isFeedbackChosenFromMenu = true
}

// The menu hands focus back to its trigger as it closes; after Feedback it goes into the panel instead.
const onActionsMenuCloseAutoFocus = async (event: Event) => {
  if (!isFeedbackChosenFromMenu) {
    return
  }
  isFeedbackChosenFromMenu = false
  event.preventDefault()
  await nextTick()
  feedbackPanel.value?.focus()
}

watch(isFeedbackOpen, async open => {
  if (open) {
    return
  }
  await nextTick()
  focusFirstConnected(actionsTrigger.value?.$el)
})

// The highlight popup in the report body opens the same panel with the highlighted text.
const feedbackPanelAnchor = ref<HTMLElement | null>(null)
provide(OPEN_REPORT_FEEDBACK_KEY, async (quotedText: string) => {
  feedbackQuotedText.value = quotedText
  isFeedbackOpen.value = true
  await nextTick()
  feedbackPanelAnchor.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  feedbackPanel.value?.focus({ preventScroll: true })
})

// Mirror the table's mutual-exclusivity rule: a row that's still cancellable
// shouldn't also offer Delete. Inside the detail menu the same logic keeps
// the dropdown consistent with the list view's row affordances.
const canDelete = computed(
  () => !!executionData.value && !canCancel.value,
)

const onDelete = () => {
  if (!executionData.value) return
  deleteReport.prompt({
    id,
    title: reportTitle(executionData.value),
  })
}

const onCancel = () => {
  if (!executionData.value) {
    return
  }
  cancelExecution.prompt({
    id,
    title: reportTitle(executionData.value),
  })
}

// Resolve the canonical Workflow definition id from the execution's
// WorkflowTypeId — mirrors what /workflows/create does on submit. The
// `workflowId` on a WorkflowExecution is a self-referencing snapshot id
// (== execution id) and pointing the start endpoint at it would re-run the
// frozen snapshot instead of starting a fresh execution from the canonical
// definition.
const workflowTypeIdRef = computed(() => executionData.value?.workflowTypeId ?? null)
const { data: reRunWorkflowSummary } = API.Workflows.useGetWorkflowSummaryByType(
  workflowTypeIdRef,
  { enabled: computed(() => !!workflowTypeIdRef.value) },
)

// Get the username (analystName) from execution data for resolution
const usernamesToResolve = computed(() => {
  if (executionData?.value?.analystName) {
    return [executionData.value.analystName]
  }
  return []
})

// Use the resolve endpoint which returns full names for usernames
const { data: resolvedUsers } = API.Users.useResolveUsers(usernamesToResolve)

// Wrap resolved users in a format compatible with WorkflowCredits component
const users = computed(() => ({
  value: resolvedUsers?.value || [],
}))


const sideMenuExpanded = ref(false)
const showPdfViewer = ref(false)
const selectedFile = ref<WorkflowExecutionDocument | null>(null)
const REPORT_EXPORTS: { format: ReportExportFormat; labelKey: string; extension: string }[] = [
  { format: 'word', labelKey: 'page.workflowDetails.actions.exportWord', extension: 'docx' },
  { format: 'markdown', labelKey: 'page.workflowDetails.actions.exportMarkdown', extension: 'md' },
]
const reportExports = REPORT_EXPORTS.map(reportExport => ({
  ...reportExport,
  ...useBundleDownload(
    API.WorkflowReports.useExportWorkflowReport(id),
    `workflow-${id}.${reportExport.extension}`,
    'page.workflowDetails.actions.exportFailed',
  ),
}))
const isExportingReport = computed(() => reportExports.some(reportExport => reportExport.isPending.value))

// Show the button as long as the run isn't still executing — mirrors the
// "not running" gate the other header buttons use. Inside the playground
// itself we further filter to AI Query blocks that have a captured prompt,
// so in-flight blocks are invisible there even if the run as a whole is
// settled with partial results.
const canTestSignature = computed(() => {
  const state = executionData.value?.workflowState
  const isRunning
    = state === WorkflowState.Running
    || state === WorkflowState.ProcessingDocuments
    || state === WorkflowState.Queued
  if (isRunning) {
    return false
  }
  return canReviewSignatures.value
})

function onTestSignature() {
  const workflowTypeId = executionData.value?.workflowTypeId
  if (!workflowTypeId) {
    return
  }
  router.push({
    name: 'SignatureSettings',
    params: { workflowTypeId },
    query: { testExecutionId: id },
  })
}

// Workflow-bundle download — same gate (operators) and same composable
// the standalone DownloadExecutedWorkflowButton used. Inlined here so the
// menu item can sit alongside the other utility actions in one overflow.
const { isOperator, isSuperAdmin } = useCurrentUserRoles()

const { download: downloadExecutedWorkflow } = useDownloadExecutedWorkflow()
function onDownloadExecutedWorkflow() {
  if (!isOperator.value) {
    return
  }
  void downloadExecutedWorkflow({ executionId: id })
}

const onFilePreview = (doc: WorkflowExecutionDocument) => {
  selectedFile.value = doc
  showPdfViewer.value = true
}

const onFileDownload = async (doc: WorkflowExecutionDocument) => {
  try {
    const response = await getFile({
      fileId: doc.fileId,
      disposition: 'download',
    })

    const blob = new Blob([response.data])
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = fullFileName(doc)
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
  } catch (error) {
    console.error('Failed to download file:', error)
  }
}

const onSaveParagraph = (elData: { id: string; [key: string]: unknown }) => {
  const { id: paragraphId, ...params } = elData

  updateReportParagraph({
    id,
    paragraphId,
    params,
  })
}

const onRemoveParagraph = (paragraphId: string) => {
  deleteReportParagraph({ id, paragraphId })
}

const selectedSectionId = ref<string | null>(null)

const sections = computed<ReportSection[]>(
  () => (outputData.value ?? []).filter(s => (s.paragraphs?.length ?? 0) > 0),
)

const detailsActivity = computed(() =>
  executionData.value?.workflowState === WorkflowState.Running
    ? activityViewOf(executionData.value.workflowActivity)
    : null,
)

const sawExecutionBeforeActivity = ref(false)
let observedExecutionId: string | null = null
watch(
  () => executionData.value,
  execution => {
    if (!execution) {
      return
    }
    if (execution.id !== observedExecutionId) {
      observedExecutionId = execution.id
      sawExecutionBeforeActivity.value = false
    }
    if (!execution.workflowActivity) {
      sawExecutionBeforeActivity.value = true
    }
  },
  { immediate: true },
)

const workflowOutput = computed(() => {
  if (!executionData.value) return null
  return { ...executionData.value, title: reportTitle(executionData.value) }
})

useItemTitle(() => workflowOutput.value?.title)

const dataOutputs = computed(() => dataOutputData.value ?? [])

// By position: a workflow can name two outputs alike, and both stay reachable.
const selectedDataOutputIndex = ref<number | null>(null)

const activeDataOutput = computed(() => {
  if (selectedDataOutputIndex.value !== null) {
    return dataOutputs.value[selectedDataOutputIndex.value]
  }
  return sections.value.length === 0 ? dataOutputs.value[0] : undefined
})

const activeSection = computed(() => {
  if (activeDataOutput.value) {
    return undefined
  }
  if (!selectedSectionId.value) {
    return sections.value.length > 0 ? sections.value[0] : undefined
  }
  return sections.value.find(x => x.id === selectedSectionId.value)
})

const activeParagraphId = ref<string | null>(null)

const onNavItemClick = (sectionId: string) => {
  if (activeSection.value?.id === sectionId) return
  selectedSectionId.value = sectionId
  selectedDataOutputIndex.value = null
  activeParagraphId.value = null
}

const onDataOutputClick = (index: number) => {
  selectedDataOutputIndex.value = index
  activeParagraphId.value = null
}

const onSubsectionClick = async (sectionId: string, paragraphId: string) => {
  if (activeSection.value?.id !== sectionId) {
    selectedSectionId.value = sectionId
    selectedDataOutputIndex.value = null
    await nextTick()
  }
  activeParagraphId.value = paragraphId
  document
    .getElementById(`subsection-${paragraphId}`)
    ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

const isLoading = computed(
  () => isLoadingOutput.value || isLoadingExecution.value,
)

const isError = computed(
  () => isErrorOutput.value || isErrorExecution.value,
)

const contentRefusal = computed(() => refusalDetail(outputError.value))

const runFailed = computed(() => executionData.value?.workflowState === WorkflowState.Failed)

const gridStyles = computed(() => gridContainerClass(activeSection.value?.layout))

const isGridLayout = computed(() => !!gridStyles.value)

const getElementGridStyles = (element: { order?: number }) =>
  gridItemClass(activeSection.value?.layout, element.order)

const collapseNeeded = computed(() => sections.value.length > 5)

const onViewLogs = () => {
  router.push({
    name: 'ExecutionLogs',
    params: { id },
  })
}

// Mirror `/workflows/create`'s gating: only offer Re-run when the resolved
// canonical workflow is still available. If the workflow type was retired
// since the source execution ran, posting its id would be a noop or backend
// error — hide the option instead of letting the user discover that.
const canReRun = computed(
  () =>
    has(CAPABILITIES.RUN)
    && !!executionData.value
    && !!reRunWorkflowSummary.value?.id
    && reRunWorkflowSummary.value.isAvailable === true,
)

// Re-run is a two-step now: open the choice dialog from the menu, the user
// picks "Restart with same inputs" or "Edit inputs first", and we either fire
// the create mutation or hand a prefill off to /workflows/create.
const showReRunChoice = ref(false)
const prefillStore = useCreateWorkflowPrefillStore()

const onReRunClick = () => {
  showReRunChoice.value = true
}

const openCreateForm = (buildPrefill: typeof buildReRunPrefill) => {
  if (!executionData.value) {
    return
  }
  const canonicalId = reRunWorkflowSummary.value?.id
  if (!canonicalId) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: $t('page.workflowDetails.actions.reRunMissingWorkflow'),
    })
    return
  }

  prefillStore.set(buildPrefill(executionData.value, canonicalId))
  router.push({ name: 'CreateWorkflow' })
}

const fetchReRunInputs = async (executionId: string) => {
  isCheckingReRun.value = true
  try {
    return await loadReRunInputs(() => API.WorkflowExecutions.service.getReRunInputs(executionId))
  } finally {
    isCheckingReRun.value = false
  }
}

const onReRunEdit = async () => {
  if (!executionData.value || isReRunning.value) {
    return
  }
  try {
    const inputs = await fetchReRunInputs(executionData.value.id)
    openCreateForm((execution, canonicalId) => planEdit(execution, canonicalId, inputs))
  } catch (err: unknown) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: reRunFailureMessage(err, $t),
    })
  }
}

const onChangeDocuments = () => openCreateForm(buildChangeDocumentsPrefill)

const onReRun = async () => {
  if (!executionData.value || isReRunning.value) {
    return
  }

  const execution = executionData.value
  const canonicalId = reRunWorkflowSummary.value?.id
  if (!canonicalId) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: $t('page.workflowDetails.actions.reRunMissingWorkflow'),
    })
    return
  }

  try {
    const inputs = await fetchReRunInputs(execution.id)
    const plan = planRestart(execution, canonicalId, inputs)
    if (plan.kind === 'form') {
      prefillStore.set(plan.prefill)
      router.push({ name: 'CreateWorkflow' })
      return
    }

    try {
      await createWorkflow(plan.payload)
    } catch (err) {
      if (!isVariableRefusal(err)) {
        throw err
      }
      prefillStore.set(variableRefusalPrefill(execution, canonicalId))
      toast({ class: 'text-sm', description: $t('page.workflowDetails.actions.reRunWorkflowChanged') })
      router.push({ name: 'CreateWorkflow' })
      return
    }
    toast({
      class: 'text-sm',
      description: reRunStartedMessage(plan.changes, $t),
      duration: plan.changes ? 6000 : undefined,
    })

    // Match the post-submit destination of /workflows/create so users land
    // in the same place regardless of how they kicked off the run.
    router.push({ name: 'Home' })
  } catch (err: unknown) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: reRunFailureMessage(err, $t),
    })
  }
}
</script>

<template>
  <div class="page-view">
    <div v-if="isLoading" class="loading-container">
      <h1 class="sr-only">{{ $t('page.workflowDetails.title') }}</h1>
      <IniaLoader class="w-8" />
      <p class="loading-text">{{ $t(`page.workflowDetails.loading`) }}</p>
    </div>

    <div v-else-if="isError" class="error-container">
      <div class="error-message">
        <template v-if="contentRefusal">
          <h1>{{ $t('page.workflowDetails.contentRefused') }}</h1>
          <p data-testid="report-content-refusal">{{ contentRefusal }}</p>
          <RunFailureNotice
            v-if="runFailed"
            class="refused-run-failure"
            :failure-kind="executionData?.failureKind"
            :failure-detail="executionData?.failureDetail"
            :can-act="canReRun"
            @change-documents="onChangeDocuments"
            @run-again="onReRunClick"
          />
        </template>
        <template v-else>
          <h1>{{ $t('page.workflowDetails.loadError') }}</h1>
          <p>{{ $t('page.workflowDetails.loadErrorHint') }}</p>
        </template>
        <Button variant="outline" as-child>
          <RouterLink :to="{ name: 'WorkflowReports' }">
            {{ $t('page.workflowDetails.backToReports') }}
          </RouterLink>
        </Button>
      </div>
    </div>

    <div v-else class="workflow-details-container">
      <div class="workflow-header-section">
        <div class="workflow-info">
          <div class="title-row">
            <div class="title-actions">
              <h1 class="page-title">
                {{ workflowOutput?.title }}
              </h1>

              <component
                :is="action"
                v-for="(action, index) in extensionPoints.reportActions"
                :key="index"
                :report="executionData"
              />

              <!-- Status next to the title: title + state is the canonical
                   "entity header" pattern (GitHub PRs, Linear, Jira). -->
              <WorkflowStatusPill
                v-if="executionData?.workflowState"
                :status="executionData.workflowState"
              />
              <ReplayProvenanceBadge
                :freeze-source-execution-id="executionData?.freezeSourceExecutionId"
                :freeze-replayed-count="executionData?.freezeReplayedCount"
                :freeze-eligible-count="executionData?.freezeEligibleCount"
                :workflow-state="executionData?.workflowState"
              />

              <!-- Single labelled trigger collapses every header action into
                   one menu. Reads obviously as a dropdown thanks to the text
                   "Actions" + chevron-down. No action is treated as more
                   important than the others — they're all peers inside the
                   menu. -->
              <div class="action-buttons">
                <DropdownMenu @update:open="onActionsMenuOpenChange">
                  <DropdownMenuTrigger as-child>
                    <Button ref="actionsTrigger" variant="outline" size="sm">
                      Actions
                      <IniaIcon name="chevron-down" classes="w-3 h-3 ml-2" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="start" class="w-56" @close-auto-focus="onActionsMenuCloseAutoFocus">
                    <!-- Cancel sits at the top because it's the only useful
                         action while a run is in flight, and a separator
                         splits it from the post-completion utilities below.
                         Destructive styling makes the consequence obvious. -->
                    <template v-if="canCancel">
                      <DropdownMenuItem
                        :disabled="cancelExecution.isPending.value"
                        class="cancel-run-item"
                        @select="onCancel"
                      >
                        <IniaIcon name="stop" classes="w-4 h-4 mr-2" />
                        <template v-if="cancelExecution.isPending.value">{{ $t('shared.cancelExecution.labelPending') }}</template>
                        <template v-else>{{ $t('shared.cancelExecution.label') }}</template>
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                    </template>
                    <DropdownMenuItem
                      v-if="canReRun"
                      :disabled="isReRunning"
                      @select="onReRunClick"
                    >
                      <IniaIcon name="repeat" classes="w-4 h-4 mr-2" />
                      <template v-if="isReRunning">{{ $t('page.workflowDetails.actions.reRunning') }}</template>
                      <template v-else>{{ $t('page.workflowDetails.actions.reRun') }}</template>
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      v-for="reportExport in reportExports"
                      :key="reportExport.format"
                      :disabled="isExportingReport"
                      @select="reportExport.download(reportExport.format)"
                    >
                      <IniaIcon name="download" classes="w-4 h-4 mr-2" />
                      <template v-if="reportExport.isPending.value">{{ $t('page.workflowDetails.actions.exporting') }}</template>
                      <template v-else>{{ $t(reportExport.labelKey) }}</template>
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      v-if="canTestSignature"
                      class="signatures-item"
                      @select="onTestSignature"
                    >
                      <IniaIcon name="signature" classes="w-4 h-4 mr-2 signatures-item__icon" />
                      <span class="signatures-item__label">
                        {{ $t('page.signatures.test.entryPoint') }}
                      </span>
                    </DropdownMenuItem>
                    <DropdownMenuItem v-if="canReadLogs" @select="onViewLogs">
                      <IniaIcon name="file-lines" classes="w-4 h-4 mr-2" />
                      View Logs
                    </DropdownMenuItem>
                    <DropdownMenuItem data-testid="feedback-menu-item" @select="onOpenFeedback">
                      <IniaIcon name="comments" classes="w-4 h-4 mr-2" />
                      {{ $t('page.workflowDetails.feedback.action') }}
                    </DropdownMenuItem>
                    <component
                      :is="item"
                      v-for="(item, index) in extensionPoints.reportMenuItems"
                      :key="index"
                      :report="executionData"
                    />
                    <DropdownMenuItem
                      v-if="isSuperAdmin && has(CAPABILITIES.RUN_EVIDENCE)"
                      data-testid="run-cost-menu-item"
                      @select="isRunCostOpen = true"
                    >
                      <IniaIcon name="file-lines" classes="w-4 h-4 mr-2" />
                      {{ $t('page.workflowDetails.runCost.action') }}
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      v-if="isOperator && has(CAPABILITIES.RUN_EVIDENCE)"
                      @select="onDownloadExecutedWorkflow"
                    >
                      <IniaIcon name="download" classes="w-4 h-4 mr-2" />
                      Download workflow
                    </DropdownMenuItem>
                    <!-- Delete sits at the bottom and is destructive-styled
                         to mirror Cancel's treatment up top. The two hit
                         different endpoints — Cancel PATCHes
                         /workflow-executions/{id}/state to flip the run to
                         Cancelled (in-flight only), Delete DELETEs the
                         report entirely (settled states only) — but at
                         this menu's level both are "remove this from the
                         user's flight deck". -->
                    <template v-if="canDelete">
                      <DropdownMenuSeparator />
                      <DropdownMenuItem
                        :disabled="deleteReport.isPending.value"
                        class="cancel-run-item"
                        @select="onDelete"
                      >
                        <IniaIcon name="trash" classes="w-4 h-4 mr-2" />
                        <template v-if="deleteReport.isPending.value">{{ $t('shared.deleteReport.labelPending') }}</template>
                        <template v-else>{{ $t('shared.deleteReport.label') }}</template>
                      </DropdownMenuItem>
                    </template>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </div>
          </div>
          <div v-if="executionData?.title" class="workflow-type">
            {{ workflowOutput?.workflowTypeName }}
          </div>
          <component
            :is="notice"
            v-for="(notice, index) in extensionPoints.reportNotices"
            :key="index"
          />
          <RunFailureNotice
            v-if="runFailed"
            class="run-failure"
            :failure-kind="executionData?.failureKind"
            :failure-detail="executionData?.failureDetail"
            :can-act="canReRun"
            @change-documents="onChangeDocuments"
            @run-again="onReRunClick"
          />
          <ModelRefusalNotice class="model-refusal" :execution-id="id" :logs="executionLogs" />
          <WorkflowActivityLine
            v-if="detailsActivity"
            class="workflow-activity"
            :entry="detailsActivity"
            :watched-from-start="sawExecutionBeforeActivity"
          />
        </div>

        <WorkflowCredits
          class="workflow-credits"
          :users="users"
          :execution-data="executionData"
          @preview="onFilePreview"
          @download="onFileDownload"
        />
      </div>

      <!-- Mounted only while open, so the feedback query runs when a reader asks for it. -->
      <div ref="feedbackPanelAnchor" />
      <ReportFeedbackPanel
        v-if="isFeedbackOpen"
        ref="feedbackPanel"
        v-model:open="isFeedbackOpen"
        :execution-id="id"
        :quoted-text="feedbackQuotedText"
      />

      <div ref="reportBodyRef" class="content-layout" v-if="activeSection || activeDataOutput">
        <nav class="sidebar-nav" :aria-label="$t('page.workflowDetails.sectionNav.label')">
          <div
            class="nav-menu"
            :class="!sideMenuExpanded && collapseNeeded && 'collapsed-menu'"
          >
            <div
              v-for="(section, sectionIdx) in sections"
              :key="sectionIdx"
              class="nav-section"
            >
              <button
                type="button"
                class="nav-section-link"
                :class="{
                  'nav-section-active': activeSection?.id === section.id,
                  'nav-section-no-children': !section.paragraphs?.length,
                  'has-indicator': section.paragraphs?.length
                }"
                :aria-current="activeSection?.id === section.id ? 'true' : undefined"
                @click="onNavItemClick(section.id)"
              >
                <span class="section-title">{{ section.title }}</span>
              </button>

              <div
                v-if="section.paragraphs?.length"
                class="subsection-container"
              >
                <div class="subsection-line"></div>
                <div class="subsection-list">
                  <template v-for="(paragraph, paragraphIdx) in section.paragraphs" :key="paragraphIdx">
                    <button
                      v-if="paragraph.subtitle"
                      type="button"
                      class="subsection-link"
                      :class="{ 'subsection-active': activeParagraphId === paragraph.id }"
                      @click="onSubsectionClick(section.id, paragraph.id)"
                    >
                      <span>{{ paragraph.subtitle }}</span>
                    </button>
                  </template>
                </div>
              </div>
            </div>

            <div v-if="dataOutputs.length > 0" class="nav-section nav-data-group">
              <button
                type="button"
                class="nav-section-link has-indicator"
                :class="{ 'nav-section-active': !!activeDataOutput }"
                :aria-current="activeDataOutput ? 'true' : undefined"
                @click="onDataOutputClick(0)"
              >
                <span class="section-title">{{ $t('page.workflowDetails.dataOutputs.title') }}</span>
              </button>
              <div class="subsection-container">
                <div class="subsection-line"></div>
                <div class="subsection-list">
                  <button
                    v-for="(output, outputIndex) in dataOutputs"
                    :key="outputIndex"
                    type="button"
                    class="subsection-link"
                    :class="{ 'subsection-active': activeDataOutput === output }"
                    @click="onDataOutputClick(outputIndex)"
                  >
                    <span>{{ output.name }}</span>
                  </button>
                </div>
              </div>
            </div>
          </div>

          <button
            v-if="collapseNeeded"
            type="button"
            class="nav-expand-toggle mt-6 flex md:hidden w-full items-center justify-center space-x-2 cursor-pointer"
            :aria-expanded="sideMenuExpanded"
            @click="sideMenuExpanded = !sideMenuExpanded"
          >
            <span>{{ $t(sideMenuExpanded ? 'page.workflowDetails.sectionNav.collapse' : 'page.workflowDetails.sectionNav.expand') }}</span>
            <IniaIcon
              :name="sideMenuExpanded ? 'caret-up' : 'caret-down'"
              class="text-lg"
            />
          </button>
        </nav>

        <div
          class="w-full h-[80px] md:h-auto md:w-[80px] flex items-center justify-center"
        >
          <div class="w-full h-[2px] md:w-[2px] md:h-full bg-white" />
        </div>

        <div class="w-full md:w-[calc(100%-336px)] pb-20">
          <div class="card-panel">
          <div v-if="activeDataOutput" class="data-output-panel">
            <div class="section-heading-row">
              <h2 class="section-heading">
                {{ $t('page.workflowDetails.dataOutputs.title') }}
              </h2>
              <Badge
                variant="outline"
                class="whitespace-nowrap text-[var(--text-secondary)] cursor-help"
                :title="$t('page.workflowDetails.dataOutputs.sentToSystemsHint')"
              >
                {{ $t('page.workflowDetails.dataOutputs.sentToSystems') }}
              </Badge>
              <Badge
                variant="outline"
                class="whitespace-nowrap text-[var(--text-secondary)] cursor-help"
                :title="$t('page.workflowDetails.dataOutputs.notInExportsHint')"
              >
                {{ $t('page.workflowDetails.notInExports') }}
              </Badge>
            </div>
            <p class="data-output-intro">{{ $t('page.workflowDetails.dataOutputs.intro') }}</p>
            <ReportDataOutput :key="selectedDataOutputIndex ?? 0" :output="activeDataOutput" />
          </div>
          <div v-else-if="activeSection" class="transition-all duration-300 scroll-mt-8">
            <div class="section-heading-row">
              <h2 class="section-heading">
                {{ activeSection.title }}
              </h2>
              <Badge
                v-if="activeSection.excludeFromWordExport"
                variant="outline"
                data-testid="not-in-exports"
                class="whitespace-nowrap text-[var(--text-secondary)] cursor-help"
                :title="$t('page.workflowDetails.notInExportsHint')"
              >
                {{ $t('page.workflowDetails.notInExports') }}
              </Badge>
            </div>
            <!-- Grid Layout Mode -->
            <div v-if="isGridLayout" :class="gridStyles" class="workflow-grid">
              <WorkflowElement
                v-for="element in activeSection.paragraphs"
                :key="activeSection.id + element?.id"
                :id="`subsection-${element?.id}`"
                :element="element"
                :execution-id="id"
                :section-id="activeSection.id"
                :class="getElementGridStyles(element)"
                class="grid-item scroll-mt-8"
                @save="onSaveParagraph"
                @remove="onRemoveParagraph"
              />
            </div>

            <!-- Sequential Layout Mode (Default) -->
            <div v-else class="workflow-sequential">
              <WorkflowElement
                v-for="element in activeSection.paragraphs"
                :key="activeSection.id + element?.id"
                :id="`subsection-${element?.id}`"
                :element="element"
                :execution-id="id"
                :section-id="activeSection.id"
                class="workflow-element-spacing scroll-mt-8"
                @save="onSaveParagraph"
                @remove="onRemoveParagraph"
              />
            </div>
          </div>
          </div>
        </div>
      </div>
    </div>

    <FilePreviewDialog
      v-if="selectedFile"
      v-model:open="showPdfViewer"
      :file-id="selectedFile.fileId"
      :filename="selectedFile.name"
    />

    <CancelExecutionDialog
      v-model="cancelExecution.showDialog.value"
      @confirm="cancelExecution.confirm"
    />

    <ReRunChoiceDialog
      v-model="showReRunChoice"
      @restart="onReRun"
      @edit="onReRunEdit"
    />

    <RunCostDialog
      v-if="isSuperAdmin"
      v-model:open="isRunCostOpen"
      :execution-id="id ?? ''"
    />
    <DeleteReportDialog
      v-model="deleteReport.showDialog.value"
      @confirm="deleteReport.confirm"
    />
  </div>
</template>

<style scoped>
/* ========================================
   WORKFLOW DETAILS - COMPONENT-SPECIFIC STYLES
   Only unique styles for this complex view
   ======================================== */

/* Loading state */
.loading-container {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-sm);
  min-height: 144px;
}

.loading-text {
  font-size: 18px;
  color: var(--text-primary);
}

/* Container */
.workflow-details-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

/* Header Section - unique workflow header with credits */
/* The details card wraps below the title when, beside a 37.5rem card, the title would get less than 24rem. */
.workflow-header-section {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: var(--spacing-sm);
}

.workflow-info {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  flex: 1 1 24rem;
}

.workflow-credits {
  flex: 0 1 37.5rem;
}

.title-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.title-actions .page-title {
  overflow-wrap: anywhere;
}

.title-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-xs) var(--spacing-md);
}

.action-buttons {
  display: flex;
  align-items: center;
  /* Larger gap between groups than within a group — visually splits
     "inspect / iterate" actions from "export / download" actions. */
  gap: var(--spacing-lg, 1.5rem);
  flex-wrap: wrap;
}

.action-buttons__group {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.workflow-activity {
  margin-top: 0.5rem;
}

.run-failure {
  margin-top: 0.75rem;
  max-width: 48rem;
}

.refused-run-failure {
  max-width: 40rem;
  margin: 0 auto var(--spacing-md);
  text-align: left;
}

.refused-run-failure :deep(p) {
  margin-bottom: 0;
}

.model-refusal {
  margin-top: 0.75rem;
  max-width: 48rem;
}

.workflow-type {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-secondary);
}

/* Content Layout - unique 3-column layout */
.content-layout {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

@media (min-width: 768px) {
  .content-layout {
    flex-direction: row;
  }
}

.sidebar-nav {
  width: 100%;
}

@media (min-width: 768px) {
  .sidebar-nav {
    width: 256px;
  }
}

.nav-menu {
  display: flex;
  flex-direction: column;
  gap: 0px;
  position: relative;
}

/* Navigation Sections */
.nav-section {
  margin-bottom: 0px;
}

.nav-section-link {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  width: 100%;
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-small);
  font-size: 15px;
  font-weight: 600;
  text-align: left;
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  position: relative;
}

.nav-section-link:hover {
  background: rgba(255, 255, 255, 0.05);
}

.nav-section-active {
  background: var(--bg-2);
  border-left: 3px solid var(--accent-indigo-from);
}

.nav-section-no-children {
  padding-left: var(--spacing-sm);
}

.nav-data-group {
  margin-top: var(--spacing-xs);
  padding-top: var(--spacing-xs);
  border-top: 1px solid var(--stroke);
}

.data-output-intro {
  margin: 0 0 var(--spacing-sm);
  color: var(--text-secondary);
  font-size: 14px;
}

.section-title {
  flex: 1;
  line-height: 1.4;
}

.has-indicator .section-title {
  position: relative;
  padding-left: calc(6px + var(--spacing-sm));
}

.has-indicator .section-title::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0.7em;
  transform: translateY(-50%);
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--primary);
}

/* Subsections */
.subsection-container {
  position: relative;
  margin-top: var(--spacing-xs);
  margin-left: var(--spacing-sm);
  padding-left: var(--spacing-sm);
}

.subsection-line {
  position: absolute;
  left: 3px;
  top: 0;
  bottom: 0;
  width: 1px;
  background: var(--stroke);
}

.subsection-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.subsection-link {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  width: 100%;
  padding: 6px var(--spacing-xs);
  border-radius: var(--radius-small);
  font-size: 13px;
  text-align: left;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  line-height: 1.4;
}

.nav-section-link:focus-visible,
.subsection-link:focus-visible,
.nav-expand-toggle:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: 2px;
}

.subsection-link:hover {
  color: var(--text-primary);
  background: rgba(255, 255, 255, 0.03);
}

.subsection-active {
  color: var(--accent-indigo-from);
  font-weight: 600;
  background: var(--bg-2);
}

.subsection-bullet {
  color: var(--text-muted);
  flex-shrink: 0;
  margin-top: 1px;
}

/* Grid Item Override */
.grid-item {
  margin-bottom: 0 !important;
}

/* Collapsed Menu Mobile */
@media (max-width: 767px) {
  .collapsed-menu {
    max-height: 200px;
    overflow-y: hidden;
    overflow: hidden;
    position: relative;
    transition: all 300ms;
  }

  .collapsed-menu::after {
    content: '';
    position: absolute;
    bottom: 0;
    left: 0;
    width: 100%;
    height: 3rem;
    background: linear-gradient(to bottom, transparent, var(--bg-0));
  }
}

@media (min-width: 768px) {
  .collapsed-menu {
    max-height: none;
    overflow: visible;
  }
}

/* Section Heading */
.section-heading-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
}

.section-heading {
  font-size: 32px;
  font-weight: 700;
  color: var(--text-primary);
  letter-spacing: -0.03em;
  line-height: 1.2;
  user-select: none;
}

/* Workflow Element Spacing */
.workflow-element-spacing {
  margin-bottom: var(--spacing-lg);
}

.workflow-element-spacing:last-child {
  margin-bottom: 0;
}

/* Deep overrides for child components - exclude buttons */
:deep(.text-white):not(button):not(button *) {
  color: var(--text-primary) !important;
}

:deep(.text-sm):not(button):not(button *) {
  color: var(--text-secondary);
}

:deep(.bg-background):not(button):not(button *) {
  background: var(--bg-1) !important;
}

:deep(.border-border):not(button):not(button *) {
  border-color: var(--stroke) !important;
}
</style>

<!-- Unscoped — DropdownMenuContent is portaled out of this component's
     scoped-style tree by reka-ui, so the trademark styling for the
     Signatures menu item has to live in a global block. -->
<style>
.signatures-item .signatures-item__label {
  font-family: 'Georgia', 'Times New Roman', serif;
  font-style: italic;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--text-primary);
}

.signatures-item .signatures-item__icon {
  color: var(--text-primary);
}
</style>
