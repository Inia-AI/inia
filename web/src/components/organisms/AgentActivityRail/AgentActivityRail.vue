<template>
  <!-- The floating rail is gone: a strip that follows the agent off the desk
       reads as an intrusion rather than a courtesy. This component stays mounted
       because it owns two things the desk needs — the poll that ingests agent
       activity into the store, and the detail panel the desk's presence lines
       open. Only its own visible surface was removed. -->
  <AgentActivityPanel />
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useQueries } from '@tanstack/vue-query'
import API from '@/api'
import { IN_FLIGHT_STATES } from '@/lib/workflowRefetch'
import useAgentActivityStore from '@/store/agentActivity'
import type { ODataQueryParams } from '@/api/schema'
import AgentActivityPanel from '@/components/organisms/AgentActivityPanel/AgentActivityPanel.vue'

const POLL_INTERVAL_MS = 5_000
const PRUNE_INTERVAL_MS = 60_000

const store = useAgentActivityStore()

const inFlightFilter = [...IN_FLIGHT_STATES]
  .map(state => `workflowState eq '${state}'`)
  .join(' or ')

const listParams = ref<ODataQueryParams>({
  $top: 10,
  $orderby: 'createdAt desc',
  $filter: `(${inFlightFilter})`,
})

const { data: inFlightList } = API.WorkflowReports.useGetWorkflowReports(
  listParams,
  { refetchInterval: POLL_INTERVAL_MS },
)

const inFlightIds = computed(
  () => inFlightList.value?.value?.map(summary => summary.id) ?? [],
)

// A report that finishes drops out of the in-flight list, and with it the poll
// that would have told us its agents are done — so the desk went on counting
// them as working until the entry was pruned. Each departing report earns one
// last read, under its own query key so it is a real fetch and not the settled
// state's absence being served from the cache of the poll before it.
const settlingIds = ref<string[]>([])

watch(inFlightIds, (ids, previousIds) => {
  const departed = (previousIds ?? []).filter(id => !ids.includes(id))
  if (departed.length === 0) {
    return
  }
  settlingIds.value = [
    ...new Set([...settlingIds.value.filter(id => !ids.includes(id)), ...departed]),
  ]
})

const reportQueries = useQueries({
  queries: computed(() => [
    ...inFlightIds.value.map(id => ({
      queryKey: ['workflow-reports', id],
      queryFn: () => API.WorkflowReports.service.getWorkflowReport(id),
      refetchInterval: POLL_INTERVAL_MS,
    })),
    ...settlingIds.value.map(id => ({
      queryKey: ['workflow-reports', id, 'settled'],
      queryFn: () => API.WorkflowReports.service.getWorkflowReport(id),
    })),
  ]),
})

watch(
  reportQueries,
  results => {
    const settled: string[] = []
    for (const [index, result] of results.entries()) {
      if (!result.data) {
        continue
      }
      store.ingestReport(result.data)
      if (index >= inFlightIds.value.length) {
        settled.push(result.data.id)
      }
    }
    if (settled.length > 0) {
      settlingIds.value = settlingIds.value.filter(id => !settled.includes(id))
    }
  },
  { deep: true },
)

let pruneTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  pruneTimer = setInterval(() => store.prune(), PRUNE_INTERVAL_MS)
})

onUnmounted(() => {
  if (pruneTimer) clearInterval(pruneTimer)
})

</script>
