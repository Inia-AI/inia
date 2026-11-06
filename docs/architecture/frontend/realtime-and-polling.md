---
type: explanation
status: current
description: "Documents that the Vue app learns of run progress by polling with TanStack Query, lists every poll and its interval, and states that there is no live push: the SignalR hub and its client were removed. Read when a screen does not update while a run is in flight, you add a poll, or you consider live push."
---

# Real-time Updates & Polling

Execution progress reaches the IniaAi.Web UI through **polling**, not server push. While a workflow run is in flight, the views that display it re-fetch their data on a fixed interval via TanStack Query and stop once the run reaches a terminal state.

> [!WARNING]
> **Today = polling, not push**
>
> Everything the user sees update "live" is the result of periodic HTTP re-fetches. Do not describe the UI as receiving real-time pushes.

## How progress updates reach the UI today

Progress is driven by TanStack Query's `refetchInterval` option. The reusable policy lives in `src/lib/workflowRefetch.ts`, which exports an `inFlightRefetchInterval()` factory:

```ts
const IN_FLIGHT_STATES: ReadonlySet<WorkflowState> = new Set([
  WorkflowState.Queued,
  WorkflowState.Running,
  WorkflowState.ProcessingDocuments,
])

export const inFlightRefetchInterval = <T extends { workflowState?: WorkflowState }>(
  intervalMs: number = 5_000,
) => {
  return (query: { state: { data?: T } }): number | false => {
    const state = query.state.data?.workflowState
    return state && IN_FLIGHT_STATES.has(state) ? intervalMs : false
  }
}
```

The returned function is handed to a query as its `refetchInterval`. TanStack Query calls it after each fetch with the current query state. While the cached `workflowState` is `Queued`, `Running`, or `ProcessingDocuments`, it returns the interval (default `5_000` ms) and the query keeps re-fetching; as soon as the run settles into a terminal state, it returns `false` and the polling loop halts. No manual reload is needed to pick up the final state.

### Where it is used

| View | File | Interval policy |
|---|---|---|
| Workflow execution logs | `src/views/WorkflowExecutionLogs.vue` | `inFlightRefetchInterval()` (~5s while in-flight) |
| Workflow details | `src/views/WorkflowDetails.vue` | `inFlightRefetchInterval()` (~5s while in-flight) |
| Workflow reports list | `src/views/WorkflowReports.vue` | fixed `refetchInterval: 5_000` |
| Home desk | `src/views/HomeView.vue` | fixed `POLL_INTERVAL_MS` (5 s) on its two lists, the reports in progress and the review docket |
| Agent activity rail | `src/components/organisms/AgentActivityRail/AgentActivityRail.vue` | fixed 5 s on the ten newest in-flight reports and on each of them |
| Document read watch | `useWatchDocumentProcessing` in `src/api/documents/index.ts` | 2.5 s while the document's `processingState` is still in progress |


The two single-execution views (`WorkflowExecutionLogs`, `WorkflowDetails`) use the conditional `inFlightRefetchInterval()` policy, so they self-terminate when the run finishes. The list views (`WorkflowReports`, `HomeView`) poll on a flat 5-second interval regardless of state.

### The agent activity rail

`AgentActivityRail` is mounted once in `App.vue`, on every route that is not public. It has no visible surface of its own: it owns the poll that feeds the desk's agent activity and the detail panel (`AgentActivityPanel`) that the desk's presence lines open. Every 5 seconds it lists the ten newest reports in an in-flight state (`IN_FLIGHT_STATES`) and reads each of them, and it hands every report it reads to the `agentActivity` store (`ingestReport`). A report that leaves the in-flight list gets one last read under its own query key, so the store learns that its agents finished instead of counting them as working until the entry is pruned. Every 60 seconds the rail calls the store's `prune`, which drops an entry not seen for 15 minutes. When an entry settles, the panel reads the block's final activity digest once (`API.AgentActivity.service.getBlockActivity`, with the entry's round). [State, routing and auth](state-routing-and-auth.md#pinia-stores) describes the store.

> [!TIP]
> **Why the conditional factory**
>
> A flat interval polls forever, including long after a run is done. The factory keys the interval off the cached `workflowState` so an idle, fully-settled execution generates zero background traffic, while an active one stays fresh within ~5 seconds.

## There is no live push

The platform has no server push for run progress. The execution SignalR hub (`/hubs/execution`) was removed from the API, together with the app's client composable `useSignalR.ts`, the `@microsoft/signalr` package and the web image's `/hubs/` proxy route. The hub let any signed-in user join another organization's or another user's update group, and no code ever sent on it; the client was imported nowhere. On the API, `POST /hubs/execution/negotiate` answers `404`. To make a screen follow a run, add a poll the way the views above do.

## Summary

| Concern | Status |
|---|---|
| Live progress in the UI today | Polling via TanStack Query `refetchInterval` |
| Conditional, self-terminating polling | `inFlightRefetchInterval()` in `src/lib/workflowRefetch.ts` |
| Server push (SignalR) | None: the hub, its client, the package and the `/hubs/` proxy route were removed |
