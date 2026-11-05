import { computed, unref, type ComputedRef, type MaybeRef, type Ref } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import Service, {
  type AcceptSuggestionBody,
  type CastVoteBody,
  type SubmitPendingBody,
  type SubmitPendingResponse,
} from './service'
import type { ServiceParams } from '../schema'
import type {
  SignatureHistoryFilters,
  SignatureHistoryPageDto,
  SignatureUpdate,
  WorkflowSignaturesDto,
} from './schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetWorkflowSignatures = (workflowTypeId: MaybeRef<string>) => {
    const queryKey = computed(() => ['signatures', unref(workflowTypeId)])
    return useQuery<WorkflowSignaturesDto>({
      queryKey,
      queryFn: () => this.service.getWorkflowSignatures(unref(workflowTypeId)),
      enabled: computed(() => unref(workflowTypeId).length > 0),
    })
  }

  useUpdateWorkflowSignaturesMutation = () => {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (variables: { workflowTypeId: string; updates: SignatureUpdate[] }) =>
        this.service.updateWorkflowSignatures(variables.workflowTypeId, variables.updates),
      onSuccess: (_data, variables) =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', variables.workflowTypeId],
        }),
    })
  }

  /**
   * Submit a new pending override for review. Errors (incl. 409 stale-field,
   * i.e. the field was renamed/deleted since the GET) propagate to the caller
   * so the existing outdated-modal dispatcher can react. On success the
   * signatures query is invalidated to surface the new pending row inline.
   *
   * Accepts `MaybeRef<string>` so the editor can pass its reactive route param
   * directly. The same component instance handles both /signatures and
   * /signatures/:workflowTypeId, so the id changes without a remount and a
   * captured snapshot would point at the wrong workflow.
   */
  useSubmitPendingMutation = (workflowTypeId: MaybeRef<string>) => {
    const queryClient = useQueryClient()
    return useMutation<SubmitPendingResponse, unknown, SubmitPendingBody>({
      mutationFn: (body) =>
        this.service.submitPendingOverride(unref(workflowTypeId), body),
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', unref(workflowTypeId)],
        }),
    })
  }

  /**
   * Cast an Approve or Reject vote on a pending override. HTTP errors are
   * propagated transparently — the consumer is responsible for surfacing
   * 409 (already voted / proposer-cannot-vote) etc.
   *
   * `workflowTypeId` is read at call time (not capture time) — see
   * `useSubmitPendingMutation` for the rationale.
   */
  useCastVoteMutation = (workflowTypeId: MaybeRef<string>) => {
    const queryClient = useQueryClient()
    return useMutation<
      void,
      unknown,
      { pendingOverrideId: string; body: CastVoteBody }
    >({
      mutationFn: ({ pendingOverrideId, body }) =>
        this.service.castVote(unref(workflowTypeId), pendingOverrideId, body),
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', unref(workflowTypeId)],
        }),
    })
  }

  /**
   * Accept a counter-suggestion as the proposer. Backend replaces the pending
   * value with the suggestion and resets votes; we just refetch.
   *
   * `workflowTypeId` is read at call time (not capture time) — see
   * `useSubmitPendingMutation` for the rationale.
   */
  useAcceptSuggestionMutation = (workflowTypeId: MaybeRef<string>) => {
    const queryClient = useQueryClient()
    return useMutation<
      void,
      unknown,
      { pendingOverrideId: string; body: AcceptSuggestionBody }
    >({
      mutationFn: ({ pendingOverrideId, body }) =>
        this.service.acceptSuggestion(
          unref(workflowTypeId),
          pendingOverrideId,
          body,
        ),
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', unref(workflowTypeId)],
        }),
    })
  }

  /**
   * Proposer-only. Cancels the pending override and refetches so the field
   * falls back to "default" or the prior active override.
   */
  useCancelPendingMutation = (workflowTypeId: MaybeRef<string>) => {
    const queryClient = useQueryClient()
    return useMutation<void, unknown, { pendingOverrideId: string }>({
      mutationFn: ({ pendingOverrideId }) =>
        this.service.cancelPending(unref(workflowTypeId), pendingOverrideId),
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', unref(workflowTypeId)],
        }),
    })
  }

  /**
   * Reviewer-only. Removes the caller's own vote so they can re-cast. The
   * pending itself sticks around; refetch surfaces the updated counts.
   */
  useWithdrawVoteMutation = (workflowTypeId: MaybeRef<string>) => {
    const queryClient = useQueryClient()
    return useMutation<void, unknown, { pendingOverrideId: string }>({
      mutationFn: ({ pendingOverrideId }) =>
        this.service.withdrawVote(unref(workflowTypeId), pendingOverrideId),
      onSuccess: () =>
        queryClient.invalidateQueries({
          queryKey: ['signatures', unref(workflowTypeId)],
        }),
    })
  }

  /**
   * Per-workflow override history page. Re-runs whenever `workflowTypeId` or
   * any filter ref changes; filters are read at call time so the editor can
   * pass reactive form state directly without re-binding the query.
   *
   * `placeholderData: keepPreviousData` keeps the prior page rendered while
   * the next page is in flight, so paging Next/Previous doesn't flash an
   * empty state between requests.
   */
  useGetSignatureHistory = (
    workflowTypeId: MaybeRef<string>,
    // `filters` must be reactive (a Ref or ComputedRef) — a plain object
    // mutated in place would keep the same identity, so the `unref(filters)`
    // dependency wouldn't change and tanstack-query wouldn't refetch on
    // filter updates. The narrower type prevents that foot-gun at compile
    // time. `workflowTypeId` stays `MaybeRef<string>` because string values
    // are immutable so identity-based reactivity is fine for them.
    filters: Ref<SignatureHistoryFilters> | ComputedRef<SignatureHistoryFilters>,
  ) => {
    const queryKey = computed(() => [
      'signatures',
      unref(workflowTypeId),
      'history',
      unref(filters),
    ])
    return useQuery<SignatureHistoryPageDto>({
      queryKey,
      queryFn: () =>
        this.service.getHistory(unref(workflowTypeId), unref(filters)),
      enabled: computed(() => unref(workflowTypeId).length > 0),
      placeholderData: keepPreviousData,
    })
  }
}

export { Service }
export * from './schema'
