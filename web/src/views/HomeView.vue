<script lang="ts" setup>
interface WorkflowOutputItem {
  id: string
  title?: string
  workflowState: WorkflowState
  workflowTypeId?: string
  counterpartyName?: string
  analystName: string
  createdAt?: string | Date
  startedAt?: string
  duration?: string
}

interface WaitingAsk {
  id: string
  askedBy: string
  question: string
}

import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRouter, RouterLink } from 'vue-router'
import { WorkflowOutputRouteAction, WorkflowState } from '@/enums'
import API from '@/api'
import { useI18n } from 'vue-i18n'
import { useQueries, useQueryClient } from '@tanstack/vue-query'
import { format, isToday } from 'date-fns'
import { extensionPoints } from '@/modules/extensionPoints'
import type { ODataQueryParams } from '@/api/schema'
import type { WorkflowReport } from '@/api/workflowReports/schema'
import { getDecodedToken, parseServerDate } from '@/lib/utils'
import { useToast } from '@/components/ui/toast/use-toast'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import {
  AgentPresenceLine,
  CancelExecutionDialog,
  ReportCardActions,
  ReRunChoiceDialog,
  WorkflowActivityLine,
} from '@/components/molecules'
import {
  useCancelExecution,
  isCancellableState,
} from '@/composables/useCancelExecution'
import {
  isVariableRefusal,
  loadReRunInputs,
  planEdit,
  planRestart,
  reRunFailureMessage,
  reRunStartedMessage,
  variableRefusalPrefill,
} from '@/lib/workflowReRunPlan'
import { reportTitle } from '@/lib/reportTitle'
import {
  runProgressLayoutOf,
  showsAgents,
  showsSections,
  type RunProgressLayout,
} from '@/lib/runProgress'
import { useWorkflowTypeNames } from '@/composables/useWorkflowTypeNames'
import useCreateWorkflowPrefillStore from '@/store/createWorkflowPrefill'
import useAgentActivityStore from '@/store/agentActivity'
import type { AgentRailEntry } from '@/store/agentActivity'
import { IniaLoader, IniaIcon, LiveDuration } from '@/components/atoms'
import { useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'

const POLL_INTERVAL_MS = 5_000
const CLOCK_TICK_MS = 1_000
const IN_PROGRESS_TOP = 10
const REVIEW_DOCKET_TOP = 5
const DRAFT_DETAIL_STALE_MS = 5 * 60 * 1000

const { t: $t, tm, rt } = useI18n()

const router = useRouter()

const userId = getDecodedToken()?.sub || null
const showOnlyMine = ref(true)

const userFilterClause = computed(() =>
  showOnlyMine.value && userId ? ` and userId eq '${userId}'` : '',
)

const inProgressQueryParams = computed<ODataQueryParams>(() => {
  const stateFilter = `(workflowState eq '${WorkflowState.Running}' or workflowState eq '${WorkflowState.ProcessingDocuments}' or workflowState eq '${WorkflowState.Queued}')`
  return {
    $top: IN_PROGRESS_TOP,
    $count: true,
    $orderby: 'createdAt desc',
    $filter: stateFilter + userFilterClause.value,
  }
})

const reviewDocketQueryParams = computed<ODataQueryParams>(() => {
  const stateFilter = `(workflowState eq '${WorkflowState.Draft}')`
  return {
    $top: REVIEW_DOCKET_TOP,
    $count: true,
    $orderby: 'createdAt desc',
    $filter: stateFilter + userFilterClause.value,
  }
})

const { data: inProgressData, isLoading: inProgressLoading } =
  API.WorkflowReports.useGetWorkflowReports(inProgressQueryParams, {
    refetchInterval: POLL_INTERVAL_MS,
  })

const { data: reviewDocketData, isLoading: reviewDocketLoading } =
  API.WorkflowReports.useGetWorkflowReports(reviewDocketQueryParams, {
    refetchInterval: POLL_INTERVAL_MS,
  })

const isLoading = computed(
  () => inProgressLoading.value || reviewDocketLoading.value,
)

const currentUsername = getDecodedToken()?.preferred_username || null

const uniqueAnalystUsernames = computed(() => {
  const usernames = [
    ...(inProgressData.value?.value ?? []),
    ...(reviewDocketData.value?.value ?? []),
  ]
    .map(wf => wf.analystName)
    .filter((name): name is string => !!name)
  if (currentUsername) {
    usernames.push(currentUsername)
  }

  return [...new Set(usernames)]
})

// Use the resolve endpoint which returns full names for usernames
const { data: resolvedUsers } = API.Users.useResolveUsers(
  uniqueAnalystUsernames,
)

// Fetch counterparties to resolve names
const counterpartyParams = ref({
  $top: 10000,
  $orderby: 'name asc',
})
const { data: counterparties } =
  API.Counterparties.useGetCounterparties(counterpartyParams)

const { toast } = useToast()

const { mutateAsync: deleteWorkflow } =
  API.WorkflowReports.useDeleteWorkflowReport()

const { withTypeName } = useWorkflowTypeNames()

const decorated = (rows: WorkflowReport[] | undefined): WorkflowOutputItem[] =>
  (rows ?? []).map(withTypeName).map(x => ({
    ...x,
    title: reportTitle(x),
    analystName:
      resolvedUsers?.value?.find(u => u.username === x.analystName)?.fullName ||
      x.analystName ||
      'Unknown',
    counterpartyName:
      counterparties?.value?.value?.find(c => c.id === x.counterpartyId)
        ?.name ||
      x.counterpartyName ||
      '',
  }))

const processingReports = computed(() => decorated(inProgressData.value?.value))

const draftReports = computed(() => decorated(reviewDocketData.value?.value))

// Reserved for a paused run's question, which outranks everything on the
// desk. The platform cannot produce this state yet, so nothing feeds it.
const waitingAsks: WaitingAsk[] = []

const agentStore = useAgentActivityStore()

const now = ref(Date.now())
let clockTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  clockTimer = setInterval(() => {
    now.value = Date.now()
  }, CLOCK_TICK_MS)
})

onUnmounted(() => {
  if (clockTimer) clearInterval(clockTimer)
})

const teamOf = (executionId: string): AgentRailEntry[] =>
  agentStore.railEntries.filter(entry => entry.executionId === executionId)

const isWorking = (entry: AgentRailEntry) =>
  !entry.settled && entry.displayDigest?.outcome !== 'Failed'

const deskWorkingCount = computed(
  () => agentStore.railEntries.filter(isWorking).length,
)

const greetingName = computed(() => {
  if (!currentUsername) {
    return null
  }
  const fullName = resolvedUsers?.value?.find(
    u => u.username === currentUsername,
  )?.fullName
  const firstName = (fullName ?? '').trim().split(/\s+/)[0]
  return firstName || null
})

const GREETING_KEYS = {
  morning: ['greetingMorning', 'greetingMorningPlain'],
  afternoon: ['greetingAfternoon', 'greetingAfternoonPlain'],
  evening: ['greetingEvening', 'greetingEveningPlain'],
} as const

const greetingLine = computed(() => {
  const hour = new Date(now.value).getHours()
  const timeOfDay = hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening'
  const [named, plain] = GREETING_KEYS[timeOfDay]
  return greetingName.value
    ? $t(`shared.agentActivity.desk.${named}`, { name: greetingName.value })
    : $t(`shared.agentActivity.desk.${plain}`)
})

// Both clauses take the same shape — numerals, one weight — so neither looks
// like the more important half. Spelling one count and not the other, with a
// bold on just the first, made two equal facts read as a headline and a footnote.
const premiseAgents = computed(() =>
  $t('shared.agentActivity.premiseAgents', deskWorkingCount.value),
)

const premiseReports = computed(() =>
  $t('shared.agentActivity.premiseReports', processingReports.value.length),
)

const inviteDraw = Math.random()

const premiseIdleInvite = computed(() => {
  const invites = tm('shared.agentActivity.premiseIdleInvites')
  if (!Array.isArray(invites) || invites.length === 0) {
    return ''
  }
  return rt(invites[Math.floor(inviteDraw * invites.length)])
})

const premiseQuiet = computed(() =>
  processingReports.value.length > 0
    ? premiseReports.value
    : premiseIdleInvite.value,
)

// Null where the only thing left to say is "In progress", which the section
// heading above the card already says. Queued and Processing documents are
// narrower than the heading, so they still earn a line.
const stateLineOf = (wfOutput: WorkflowOutputItem): string | null => {
  if (wfOutput.workflowState === WorkflowState.Queued) {
    return $t('shared.agentActivity.desk.queued')
  }
  if (wfOutput.workflowState === WorkflowState.ProcessingDocuments) {
    return $t('shared.agentActivity.desk.processingDocuments')
  }
  return null
}

const startedClockOf = (wfOutput: WorkflowOutputItem): string | null => {
  const startedAt = wfOutput.startedAt ?? wfOutput.createdAt
  return startedAt ? format(parseServerDate(startedAt), 'HH:mm') : null
}

const whenOf = (wfOutput: WorkflowOutputItem): string | null => {
  if (!wfOutput.createdAt) {
    return null
  }
  const createdAt = parseServerDate(wfOutput.createdAt)
  return isToday(createdAt)
    ? format(createdAt, 'HH:mm')
    : format(createdAt, 'd MMM')
}

const draftDetailQueries = useQueries({
  queries: computed(() =>
    (reviewDocketData.value?.value ?? []).map(row => ({
      queryKey: ['workflow-reports', row.id],
      queryFn: () => API.WorkflowReports.service.getWorkflowReport(row.id),
      staleTime: DRAFT_DETAIL_STALE_MS,
    })),
  ),
})

const draftAgentCounts = computed(() => {
  const counts = new Map<string, number>()
  for (const result of draftDetailQueries.value) {
    const report = result.data
    if (report?.agentActivity?.length) {
      counts.set(report.id, report.agentActivity.length)
    }
  }
  return counts
})

const receiptOf = (wfOutput: WorkflowOutputItem): string => {
  const parts: string[] = []
  if (wfOutput.counterpartyName && wfOutput.title) {
    parts.push(wfOutput.title)
  }
  const agents =
    draftAgentCounts.value.get(wfOutput.id) ?? teamOf(wfOutput.id).length
  if (agents > 0) {
    parts.push($t('shared.agentActivity.desk.agentCount', agents))
  }
  return parts.join(' · ')
}

const classicActivityOf = (wfOutput: WorkflowOutputItem) =>
  agentStore.classicActivityOf(wfOutput.id)

const progressLayoutOf = (wfOutput: WorkflowOutputItem): RunProgressLayout =>
  runProgressLayoutOf({
    agentCount: teamOf(wfOutput.id).length,
    hasSectionProgress: classicActivityOf(wfOutput) !== null,
    hasStateLine: stateLineOf(wfOutput) !== null,
  })

const rowClockOf = (wfOutput: WorkflowOutputItem): string | null => {
  const working = teamOf(wfOutput.id).filter(isWorking).length
  return working > 0
    ? $t('shared.agentActivity.desk.workingShort', { count: working })
    : null
}

const has = useCapabilities()

const onCreate = () => {
  router.push({ name: 'CreateWorkflow' })
}

const onEditClick = (wfOutput: WorkflowOutputItem) => {
  router.push({
    name: 'WorkflowDetails',
    params: { id: wfOutput.id, action: WorkflowOutputRouteAction.Edit },
  })
}

// Delete confirmation — only used for draft reports. Cancel of an in-flight
// run goes through the shared `useCancelExecution` composable so the toast,
// the API call, and the dialog copy all match the reports list and detail
// view.
const showDeleteConfirm = ref(false)
const selectedWorkflowToDelete = ref<WorkflowOutputItem | null>(null)

const promptDelete = (wfOutput: WorkflowOutputItem) => {
  selectedWorkflowToDelete.value = wfOutput
  showDeleteConfirm.value = true
}

const onConfirmDelete = async () => {
  if (!selectedWorkflowToDelete.value) return

  const name = selectedWorkflowToDelete.value.title ?? 'Workflow'

  try {
    await deleteWorkflow(selectedWorkflowToDelete.value.id)
    toast({ title: $t('page.home.toast.deleteSuccess', { name }) })
  } catch {
    toast({
      title: $t('page.home.toast.deleteError', { name }),
      variant: 'destructive',
    })
  } finally {
    showDeleteConfirm.value = false
    selectedWorkflowToDelete.value = null
  }
}

const cancelExecution = useCancelExecution()

// --- Edit & Re-run a report from a dashboard row ---
//
// Surfaced on every desk state (Queued / Running / ProcessingDocuments /
// Draft) — a queued or in-flight run can also be re-submitted with edited
// inputs (Edit & Re-run pulls from the original execution data, not the
// run-in-progress). In-progress rows also expose Cancel; docket rows also
// expose Delete. The lookup table mirrors the reports list view: a single
// in-memory map from workflowTypeId → canonical workflow id + isAvailable,
// built from the same `/workflow-categories/summaries` payload the
// create-page picker uses.
const { data: workflowCategorySummaries } =
  API.WorkflowCategories.useGetWorkflowCategorySummaries()
const workflowSummaryByType = computed(() => {
  const map = new Map<string, { id: string; isAvailable: boolean }>()
  for (const category of workflowCategorySummaries.value ?? []) {
    for (const wf of category.workflows ?? []) {
      map.set(wf.workflowTypeId, { id: wf.id, isAvailable: wf.isAvailable })
    }
  }
  return map
})

const canReRunRow = (wf: WorkflowOutputItem) => {
  if (!has(CAPABILITIES.RUN)) {
    return false
  }
  const typeId = wf.workflowTypeId
  if (!typeId) return false
  const summary = workflowSummaryByType.value.get(typeId)
  return !!summary && summary.isAvailable === true
}

const showReRunChoice = ref(false)
const reRunTarget = ref<WorkflowOutputItem | null>(null)
const queryClient = useQueryClient()
const prefillStore = useCreateWorkflowPrefillStore()
const { mutateAsync: createWorkflowExecution, isPending: isStartPending } =
  API.WorkflowExecutions.useCreateWorkflowExecution()
const isCheckingReRun = ref(false)
const isReRunPending = computed(() => isStartPending.value || isCheckingReRun.value)

const promptReRun = (wf: WorkflowOutputItem) => {
  if (isReRunPending.value) return
  reRunTarget.value = wf
  showReRunChoice.value = true
}

const fetchFullReport = (id: string): Promise<WorkflowReport> =>
  queryClient.fetchQuery({
    queryKey: ['workflow-reports', id],
    queryFn: () => API.WorkflowReports.service.getWorkflowReport(id),
  })

// Shared pre-amble for both Re-run choices: pop the dialog target, resolve
// the canonical workflow id, and run the per-choice action with the full
// execution data. Errors get the same toast treatment regardless of branch.
const withReRunContext = async (
  action: (canonicalId: string, full: WorkflowReport) => Promise<void>,
) => {
  const wf = reRunTarget.value
  if (!wf) return
  reRunTarget.value = null

  const canonical = workflowSummaryByType.value.get(wf.workflowTypeId ?? '')
  if (!canonical?.id) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: $t('page.workflowDetails.actions.reRunMissingWorkflow'),
    })
    return
  }

  try {
    const full = await fetchFullReport(wf.id)
    await action(canonical.id, full)
  } catch (err) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: reRunFailureMessage(err, $t),
    })
  }
}

const fetchReRunInputs = async (id: string) => {
  isCheckingReRun.value = true
  try {
    return await loadReRunInputs(() => API.WorkflowExecutions.service.getReRunInputs(id))
  } finally {
    isCheckingReRun.value = false
  }
}

const onReRunRestart = () =>
  withReRunContext(async (canonicalId, full) => {
    const plan = planRestart(full, canonicalId, await fetchReRunInputs(full.id))
    if (plan.kind === 'form') {
      prefillStore.set(plan.prefill)
      void router.push({ name: 'CreateWorkflow' })
      return
    }

    try {
      await createWorkflowExecution(plan.payload)
    } catch (err) {
      if (!isVariableRefusal(err)) {
        throw err
      }
      prefillStore.set(variableRefusalPrefill(full, canonicalId))
      toast({ class: 'text-sm', description: $t('page.workflowDetails.actions.reRunWorkflowChanged') })
      void router.push({ name: 'CreateWorkflow' })
      return
    }
    toast({
      class: 'text-sm',
      description: reRunStartedMessage(plan.changes, $t),
      duration: plan.changes ? 6000 : 2000,
    })
    // Stay on the dashboard — the new run will show up in the next 5s
    // refetch tick. Force-invalidate so it surfaces sooner.
    await queryClient.invalidateQueries({ queryKey: ['workflow-reports'] })
  })

const onReRunEdit = () =>
  withReRunContext(async (canonicalId, full) => {
    prefillStore.set(planEdit(full, canonicalId, await fetchReRunInputs(full.id)))
    void router.push({ name: 'CreateWorkflow' })
  })
</script>

<template>
  <div class="home-view">
    <div class="reports-panel">
      <div class="page-container">
        <!-- Header -->
        <div class="page-header-row">
          <div class="desk-brief">
            <h1 class="brief-greeting">{{ greetingLine }}</h1>
            <p v-if="!isLoading" class="brief-status">
              <template v-if="deskWorkingCount > 0">
                <template v-if="processingReports.length > 0">
                  <span>{{ premiseReports }}</span>
                  <span class="premise-sep"> · </span>
                </template>
                <span>{{ premiseAgents }}</span>
              </template>
              <template v-else>
                {{ premiseQuiet }}
              </template>
            </p>
          </div>
          <div v-if="has(CAPABILITIES.TEAM_VIEW)" class="view-toggle-pills" role="group" :aria-label="$t('page.home.viewToggle.label')">
            <button
              type="button"
              class="view-pill"
              :class="{ active: showOnlyMine }"
              :aria-pressed="showOnlyMine"
              :aria-label="$t('page.home.viewToggle.personalView')"
              :title="$t('page.home.viewToggle.personalView')"
              @click="showOnlyMine = true"
            >
              <IniaIcon name="user" />
            </button>
            <button
              type="button"
              class="view-pill"
              :class="{ active: !showOnlyMine }"
              :aria-pressed="!showOnlyMine"
              :aria-label="$t('page.home.viewToggle.teamView')"
              :title="$t('page.home.viewToggle.teamView')"
              @click="showOnlyMine = false"
            >
              <IniaIcon name="users" />
            </button>
          </div>
          <button v-if="has(CAPABILITIES.RUN)" type="button" class="btn-create-report" @click="onCreate">
            <IniaIcon name="plus" class="mr-2" />
            Create New Report
          </button>
        </div>

        <div v-if="isLoading" class="loading-state">
          <IniaLoader class="w-8" />
        </div>

        <div v-else class="desk">
          <div class="desk-main">
            <section
              v-if="waitingAsks.length > 0"
              class="strata"
              aria-labelledby="strata-waiting"
            >
              <h2 id="strata-waiting" class="strata-label">
                {{ $t('shared.agentActivity.desk.waitingOnYou') }}
              </h2>
              <article
                v-for="ask in waitingAsks"
                :key="ask.id"
                class="ask-card"
              >
                <p class="ask-from">{{ ask.askedBy }}</p>
                <p class="ask-question">“{{ ask.question }}”</p>
              </article>
            </section>

            <section class="strata" aria-labelledby="strata-progress">
              <h2 id="strata-progress" class="strata-label">
                {{ $t('shared.agentActivity.desk.inProgress') }}
              </h2>
              <div
                v-if="processingReports.length === 0"
                class="strata-idle-card"
              >
                <p class="strata-empty">
                  {{ $t(has(CAPABILITIES.RUN) ? 'shared.agentActivity.desk.emptyDeskInvite' : 'shared.agentActivity.desk.emptyDeskNoNewReports') }}
                </p>
              </div>
              <article
                v-for="wfOutput in processingReports"
                :key="wfOutput.id"
                class="ledger-row"
              >
                <div class="row-head">
                  <button
                    type="button"
                    class="row-id"
                    @click="onEditClick(wfOutput)"
                  >
                    <h3 class="row-name">
                      {{ wfOutput.counterpartyName || wfOutput.title }}
                    </h3>
                    <p
                      v-if="wfOutput.counterpartyName && wfOutput.title"
                      class="row-brief"
                    >
                      {{ wfOutput.title }}
                    </p>
                    <p class="row-meta">
                      {{ wfOutput.analystName
                      }}<template v-if="startedClockOf(wfOutput)">
                        ·
                        {{
                          $t('shared.agentActivity.desk.started', {
                            time: startedClockOf(wfOutput),
                          })
                        }}</template
                      >
                    </p>
                  </button>
                  <div class="row-side">
                    <span class="row-pulse" aria-hidden="true"></span>
                    <span class="row-clock duration-display">
                      <template v-if="rowClockOf(wfOutput)"
                        >{{ rowClockOf(wfOutput) }} ·
                      </template>
                      <LiveDuration
                        :started-at="wfOutput.startedAt"
                        :duration="wfOutput.duration"
                        :workflow-state="wfOutput.workflowState"
                        fallback=""
                      />
                    </span>
                    <ReportCardActions
                      :report-title="wfOutput.title"
                      :can-re-run="canReRunRow(wfOutput)"
                      :is-re-run-pending="isReRunPending"
                      :can-cancel="isCancellableState(wfOutput.workflowState)"
                      :is-cancel-pending="cancelExecution.isPending.value"
                      @re-run="promptReRun(wfOutput)"
                      @cancel="cancelExecution.prompt(wfOutput)"
                    />
                  </div>
                </div>

                <div
                  v-if="showsSections(progressLayoutOf(wfOutput))"
                  class="row-classic"
                >
                  <h4
                    v-if="progressLayoutOf(wfOutput) === 'both'"
                    class="row-progress-label"
                  >
                    {{ $t('shared.agentActivity.desk.sectionsHeading') }}
                  </h4>
                  <WorkflowActivityLine
                    :entry="classicActivityOf(wfOutput)!"
                    :watched-from-start="classicActivityOf(wfOutput)!.watchedFromStart"
                  />
                </div>
                <div
                  v-if="showsAgents(progressLayoutOf(wfOutput))"
                  :class="{ 'row-agents': progressLayoutOf(wfOutput) === 'both' }"
                >
                  <h4
                    v-if="progressLayoutOf(wfOutput) === 'both'"
                    class="row-progress-label"
                  >
                    {{ $t('shared.agentActivity.desk.agentsHeading') }}
                  </h4>
                  <ul class="row-team">
                    <li
                      v-for="entry in teamOf(wfOutput.id)"
                      :key="entry.key"
                      class="row-member"
                    >
                      <AgentPresenceLine
                        :entry="entry"
                        :now="now"
                        :file-name="wfOutput.counterpartyName || wfOutput.title"
                        @open="agentStore.openPanel(entry.key)"
                      />
                    </li>
                  </ul>
                </div>
                <p v-if="progressLayoutOf(wfOutput) === 'state'" class="row-quiet">
                  {{ stateLineOf(wfOutput) }}
                </p>
              </article>
            </section>
          </div>

          <aside class="desk-side">
            <section class="strata" aria-labelledby="strata-review">
              <h2 id="strata-review" class="strata-label">
                {{ $t('shared.agentActivity.desk.readyForReview') }}
              </h2>
              <div class="docket">
                <p v-if="draftReports.length === 0" class="strata-empty">
                  {{ $t('shared.agentActivity.desk.noReportsReady') }}
                </p>
                <div
                  v-for="wfOutput in draftReports"
                  :key="wfOutput.id"
                  class="docket-row"
                >
                  <button
                    type="button"
                    class="docket-main"
                    @click="onEditClick(wfOutput)"
                  >
                    <span class="docket-name">
                      {{ wfOutput.counterpartyName || wfOutput.title }}
                    </span>
                    <span class="docket-receipt">
                      {{ receiptOf(wfOutput)
                      }}<template v-if="wfOutput.duration">
                        <template v-if="receiptOf(wfOutput)"> · </template>
                        <LiveDuration
                          :duration="wfOutput.duration"
                          :workflow-state="wfOutput.workflowState"
                          fallback=""
                        />
                      </template>
                    </span>
                  </button>
                  <div class="docket-side">
                    <span v-if="whenOf(wfOutput)" class="docket-when">{{
                      whenOf(wfOutput)
                    }}</span>
                    <ReportCardActions
                      :report-title="wfOutput.title"
                      :can-re-run="canReRunRow(wfOutput)"
                      :is-re-run-pending="isReRunPending"
                      :can-delete="true"
                      @re-run="promptReRun(wfOutput)"
                      @delete="promptDelete(wfOutput)"
                    />
                  </div>
                </div>
                <RouterLink
                  class="docket-all"
                  :to="{ name: 'WorkflowReports' }"
                >
                  {{ $t('shared.agentActivity.desk.allReports') }}
                </RouterLink>
              </div>
            </section>

            <component
              :is="section"
              v-for="(section, index) in extensionPoints.homeAside"
              :key="index"
            />
          </aside>
        </div>
      </div>
    </div>

    <!-- Delete Confirmation Dialog (drafts only — cancel of an in-flight run
         is handled by the shared CancelExecutionDialog below). -->
    <ConfirmDialog
      v-model="showDeleteConfirm"
      :title="$t('page.home.confirmDeleteTitle')"
      :description="$t('page.home.confirmDeleteMessage')"
      confirm-label="Yes"
      cancel-label="No"
      @confirm="onConfirmDelete"
    />

    <CancelExecutionDialog
      v-model="cancelExecution.showDialog.value"
      @confirm="cancelExecution.confirm"
    />

    <ReRunChoiceDialog
      v-model="showReRunChoice"
      @restart="onReRunRestart"
      @edit="onReRunEdit"
    />
  </div>
</template>

<style scoped>
/* Scrolling happens in the layout's .main-content — the view itself must
   stay unclipped or long report lists get cut off at the viewport. */
.home-view {
  min-height: 100%;
  background: var(--bg-0);
}

.reports-panel {
  background: var(--bg-0);
  overflow-y: auto;
  padding: var(--spacing-lg) var(--spacing-xl);
}

.loading-state {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 128px;
}

/* The greeting is the page title: the desk addresses its owner by name,
   with the live premise as its subtitle — who is at work carries the ink,
   and the emphasis exists only while someone actually is. */
.desk-brief {
  display: flex;
  flex-direction: column;
  gap: 5px;
  min-width: 0;
  margin: 0;
}

.brief-greeting {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.35rem;
  font-weight: 650;
  letter-spacing: -0.01em;
}

.brief-status {
  margin: 0;
  color: var(--text-secondary);
  font-size: 0.95rem;
}

.premise-sep {
  color: var(--text-muted);
}

/* Two columns: the team's moves left, deliveries right. */
.desk {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 340px;
  gap: var(--spacing-xl);
  align-items: start;
}

.desk-main {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xl);
}

.desk-side {
  position: sticky;
  top: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xl);
}

.strata-empty {
  margin: 0;
  padding: var(--spacing-md) 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* An empty column still owns a container: the same bordered panel the review
   docket sits on, so both strata-labels read as peer headings over cards. */
.strata-idle-card {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  box-shadow: var(--shadow-card);
  padding: var(--spacing-sm) var(--spacing-md);
}

.strata-idle-card .strata-empty {
  padding: 0;
}

.docket .strata-empty {
  padding: var(--spacing-sm) var(--spacing-md);
}

/* Reserved for a paused run's question, the page's single amber moment. */
.ask-card {
  padding: var(--spacing-md);
  background: var(--accent-warning-light);
  border: 1px solid var(--accent-warning-border);
  border-radius: var(--radius-panel);
}

.ask-from {
  margin: 0 0 6px;
  color: var(--text-secondary);
  font-size: 0.8rem;
  font-weight: 600;
}

.ask-question {
  margin: 0;
  color: var(--text-primary);
  font-size: 0.95rem;
  line-height: 1.5;
}

/* One file per row: who it is, the people on it, the clock. */
.ledger-row {
  position: relative;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: var(--spacing-md);
  box-shadow: var(--shadow-card);
}

.row-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-sm);
}

/* The row identity is the way into the report, exactly as .docket-main is for
   a finished one. In-progress rows used to carry a separate "View Report"
   button instead; it opened the edit view, which renders nothing until the run
   has produced sections. */
.row-id {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.row-id:focus-visible {
  outline: 2px solid var(--accent-info);
  outline-offset: 2px;
  border-radius: 4px;
}

.row-id:hover .row-name {
  text-decoration: underline;
  text-underline-offset: 3px;
}

.row-name {
  margin: 0 0 2px;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.row-brief {
  margin: 0 0 4px;
  font-size: 13px;
  color: var(--text-secondary);
}

.row-meta {
  margin: 0;
  font-size: 12px;
  color: var(--text-muted);
}

.row-side {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-shrink: 0;
}

/* Liveness lives in one small local mark, not on the card's perimeter. A traced
   border is a singular device: it works on one thing because it draws the eye
   there, and a list of them competes with itself. Every row in this strata is
   in flight, so the signal has to be something several of can sit side by side
   without the page reading as agitated. */
.row-pulse {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--primary);
  flex: none;
  margin-right: -4px;
  animation: row-pulse-beat 1.8s ease-in-out infinite;
}

@keyframes row-pulse-beat {
  0%,
  100% {
    opacity: 0.25;
    transform: scale(0.8);
  }

  50% {
    opacity: 1;
    transform: scale(1);
  }
}

@media (prefers-reduced-motion: reduce) {
  .row-pulse {
    animation: none;
    opacity: 0.9;
  }
}

.row-clock {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
}

.row-classic {
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid var(--stroke);
}

.row-agents {
  margin-top: 0.75rem;
}

.row-progress-label {
  margin: 0 0 0.4rem;
  color: var(--text-secondary);
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.row-quiet {
  margin: 0;
  /* A status line is content, not chrome. --text-muted is 3.46 on a card and
     is reserved for disabled/least-emphasis. */
  color: var(--text-secondary);
  font-size: 0.85rem;
}

/* The team: parallel columns, one presence per agent. */
.row-team {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--spacing-sm);
  margin: 0;
  padding: 0;
  list-style: none;
}

.row-member {
  min-width: 0;
  display: flex;
}

/* Ready for review: deliveries with their receipts, then the library. */
.docket-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.docket-main:focus-visible {
  outline: 2px solid var(--accent-info);
  outline-offset: 2px;
  border-radius: 4px;
}

.docket-main:hover .docket-name {
  text-decoration: underline;
  text-underline-offset: 3px;
}

.docket-receipt {
  color: var(--text-muted);
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
  line-height: 1.4;
}

.docket-side {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.docket-when {
  color: var(--text-muted);
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
}

.docket-all {
  padding: var(--spacing-sm) var(--spacing-md);
  border-top: 1px solid var(--stroke);
  color: var(--text-secondary);
  font-size: 0.8rem;
  font-weight: 550;
  text-decoration: none;
}

.docket-all:hover {
  color: var(--text-primary);
  text-decoration: underline;
  text-underline-offset: 3px;
}

/* Page header - the greeting left, actions right, one masthead */
.page-header-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-xl);
}

.page-header-row .desk-brief {
  flex: 1 1 16rem;
}

.page-header-row .btn-create-report {
  margin-left: auto;
}

/* View toggle pills */
.view-toggle-pills {
  display: inline-flex;
  gap: 4px;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 10px;
  padding: 4px;
  width: fit-content;
  height: 44px;
  box-sizing: border-box;
}

.view-pill {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 8px 12px;
  font-size: 16px;
  color: var(--text-secondary);
  background: transparent;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
}

.view-pill:hover {
  color: var(--text-primary);
  background: rgba(255, 255, 255, 0.05);
}

.view-pill.active {
  color: var(--primary-foreground);
  background: var(--primary);
  box-shadow: 0 2px 6px rgba(26, 56, 80, 0.3);
}

/* Create report button - prominent CTA */
.btn-create-report {
  display: flex;
  align-items: center;
  background: var(--primary);
  color: var(--primary-foreground);
  border: none;
  border-radius: 10px;
  height: 44px;
  padding: 0 18px;
  font-size: 14px;
  font-weight: 600;
  box-shadow: 0 4px 12px rgba(26, 56, 80, 0.35);
  transition: all 0.2s ease-in-out;
  cursor: pointer;
  white-space: nowrap;
  box-sizing: border-box;
}

.btn-create-report:hover {
  transform: scale(1.03);
  box-shadow: 0 6px 16px rgba(26, 56, 80, 0.45);
}

@media (max-width: 1024px) {
  .desk {
    grid-template-columns: 1fr;
  }

  .desk-side {
    position: static;
  }
}

@media (max-width: 768px) {
  .reports-panel {
    padding: var(--spacing-md);
  }

  .row-head {
    flex-direction: column;
  }

  .row-side {
    width: 100%;
    justify-content: space-between;
  }
}

/* Scrollbar Styling */
.reports-panel::-webkit-scrollbar {
  width: 6px;
}

.reports-panel::-webkit-scrollbar-track {
  background: transparent;
}

.reports-panel::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.1);
  border-radius: 3px;
}

.reports-panel::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.15);
}
</style>
