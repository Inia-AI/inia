import { computed, unref, type ComputedRef } from 'vue'
import { useQueries, useQuery } from '@tanstack/vue-query'
import API from '@/api'
import {
  countAwaitingMyVote,
  countAwaitingProposerAction,
} from '@/views/signatures/helpers'
import type { WorkflowSignaturesDto } from '@/api/signatures/schema'
import { useCurrentUserId } from '@/composables/useCurrentUserId'
import { useCanReviewSignatures } from '@/composables/useCapabilities'

/**
 * Aggregates "fields awaiting this user's vote" across every workflow type the
 * current tenant exposes. FE-only — no new backend endpoint. Uses `useQueries`
 * to parallel-fetch each workflow's signatures (sharing the same query cache
 * as the editor's per-workflow screens, so visiting this composable after the
 * editor is cheap, and vice versa).
 *
 * Returns a reactive per-workflowType count map + a total. Both are zero for
 * users who don't have any reviewer role — the router already gates the page
 * but the nav badge should also never render for them.
 */
export function useMyReviewCounts(): {
  countsByWorkflowType: ComputedRef<Map<string, number>>
  totalCount: ComputedRef<number>
  proposerCountsByWorkflowType: ComputedRef<Map<string, number>>
  proposerTotalCount: ComputedRef<number>
  isLoading: ComputedRef<boolean>
} {
  const { userId: myUserId } = useCurrentUserId()
  const canReview = useCanReviewSignatures()

  const hasReviewerRole = computed(() => {
    void myUserId.value
    return canReview.value
  })

  // Cached under the same key as WorkflowCategories.useGetWorkflowCategorySummaries
  // so we share the fetch with other consumers.
  const categoriesQuery = useQuery({
    queryKey: ['workflow-category-summaries'],
    queryFn: () => API.WorkflowCategories.service.getWorkflowCategorySummaries(),
    enabled: hasReviewerRole,
  })

  const workflowTypeIds = computed(() => {
    const ids = new Set<string>()
    for (const category of unref(categoriesQuery.data) ?? []) {
      for (const wf of category.workflows ?? []) {
        if (wf.isAvailable) {
          ids.add(wf.workflowTypeId)
        }
      }
    }
    return [...ids]
  })

  const signaturesQueries = useQueries({
    queries: computed(() =>
      workflowTypeIds.value.map((id) => ({
        queryKey: ['signatures', id],
        queryFn: () => API.Signatures.service.getWorkflowSignatures(id),
        enabled: hasReviewerRole.value,
      })),
    ),
  })

  const countsByWorkflowType = computed(() => {
    const result = new Map<string, number>()
    if (!hasReviewerRole.value) {
      return result
    }
    const ids = workflowTypeIds.value
    signaturesQueries.value.forEach((q, i) => {
      const data = q.data as WorkflowSignaturesDto | undefined
      if (!data) {
        return
      }
      result.set(
        ids[i],
        countAwaitingMyVote(data.fields, myUserId.value, hasReviewerRole.value),
      )
    })
    return result
  })

  const totalCount = computed(() => {
    let total = 0
    for (const [, n] of countsByWorkflowType.value) {
      total += n
    }
    return total
  })

  // Counterpart of countsByWorkflowType for the proposer perspective: fields where
  // the caller is the proposer AND a reviewer has left at least one counter-
  // suggestion that's still waiting for the proposer's accept / edit / cancel.
  // Needs-review (reviewer side) and proposer-needs-action (proposer side) are
  // disjoint by construction — a user can't be both the proposer of a pending
  // and a reviewer eligible to vote on it.
  const proposerCountsByWorkflowType = computed(() => {
    const result = new Map<string, number>()
    const currentUserId = myUserId.value
    if (currentUserId === null) {
      return result
    }
    const ids = workflowTypeIds.value
    signaturesQueries.value.forEach((q, i) => {
      const data = q.data as WorkflowSignaturesDto | undefined
      if (!data) {
        return
      }
      result.set(ids[i], countAwaitingProposerAction(data.fields, currentUserId))
    })
    return result
  })

  const proposerTotalCount = computed(() => {
    let total = 0
    for (const [, n] of proposerCountsByWorkflowType.value) {
      total += n
    }
    return total
  })

  const isLoading = computed(() => {
    if (!hasReviewerRole.value) {
      return false
    }
    if (categoriesQuery.isLoading.value) {
      return true
    }
    return signaturesQueries.value.some((q) => q.isLoading)
  })

  return {
    countsByWorkflowType,
    totalCount,
    proposerCountsByWorkflowType,
    proposerTotalCount,
    isLoading,
  }
}
