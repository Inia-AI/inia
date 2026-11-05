import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type {
  SignatureHistoryFilters,
  SignatureHistoryPageDto,
  SignatureUpdate,
  SignatureVoteKind,
  UpdateSignaturesRequest,
  WorkflowSignaturesDto,
} from './schema'

export interface SubmitPendingBody {
  placeholderName: string
  proposedValue: string | null
}

export interface SubmitPendingResponse {
  pendingOverrideId: string
}

export interface CastVoteBody {
  voteKind: SignatureVoteKind
  comment: string | null
  suggestedValue: string | null
}

export interface AcceptSuggestionBody {
  voteId: string
}

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Load the full signature surface for a workflow type. Backend resolves
   * `workflowTypeId` to the current non-deleted workflow for the org; block and
   * section ids in the response are current-session Core GUIDs.
   */
  async getWorkflowSignatures(workflowTypeId: string): Promise<WorkflowSignaturesDto> {
    const { data } = await this.instance.get<WorkflowSignaturesDto>(
      this.getApiPath(`workflow-types/${workflowTypeId}/signatures`),
    )

    return data
  }

  /**
   * Batch-apply client override values, keyed by field name. A `null` `value`
   * on any update reverts that field to its staff-authored default. If any
   * payload field has been renamed/deleted (or is no longer referenced) since
   * the GET, the backend returns 409 (`Signatures.Stale`).
   */
  async updateWorkflowSignatures(
    workflowTypeId: string,
    updates: SignatureUpdate[],
  ): Promise<void> {
    const payload: UpdateSignaturesRequest = { updates }
    await this.instance.put(
      this.getApiPath(`workflow-types/${workflowTypeId}/signatures`),
      payload,
    )
  }

  /**
   * Submit a new pending override for review, keyed by field name.
   * `proposedValue: null` proposes removal of the active override. Replaces
   * any existing pending for the same field and resets its votes server-side.
   * Fails with 400 (`Signatures.UnknownField`) for a name the workflow doesn't
   * define, and 409 (`Signatures.Stale`) when the field was renamed/deleted or
   * is no longer referenced since the GET.
   */
  async submitPendingOverride(
    workflowTypeId: string,
    body: SubmitPendingBody,
  ): Promise<SubmitPendingResponse> {
    const { data } = await this.instance.post<SubmitPendingResponse>(
      this.getApiPath(`workflow-types/${workflowTypeId}/signatures/pending`),
      body,
    )
    return data
  }

  /**
   * Cast an Approve or Reject vote on a pending override. Reject votes may
   * carry a comment and an optional `suggestedValue` for the proposer to
   * accept. Returns 409 if the caller has already voted or is the proposer.
   */
  async castVote(
    workflowTypeId: string,
    pendingOverrideId: string,
    body: CastVoteBody,
  ): Promise<void> {
    await this.instance.post(
      this.getApiPath(
        `workflow-types/${workflowTypeId}/signatures/pending/${pendingOverrideId}/votes`,
      ),
      body,
    )
  }

  /**
   * Accept a counter-suggestion (proposer-only). Replaces the pending value
   * with the suggestion, clears all votes, and re-seeds the proposer's
   * auto-approve.
   */
  async acceptSuggestion(
    workflowTypeId: string,
    pendingOverrideId: string,
    body: AcceptSuggestionBody,
  ): Promise<void> {
    await this.instance.post(
      this.getApiPath(
        `workflow-types/${workflowTypeId}/signatures/pending/${pendingOverrideId}/accept-suggestion`,
      ),
      body,
    )
  }

  /**
   * Proposer-only. Withdraws a pending override before the threshold is hit.
   * Escape hatch for stalemates in small tenants.
   */
  async cancelPending(
    workflowTypeId: string,
    pendingOverrideId: string,
  ): Promise<void> {
    await this.instance.delete(
      this.getApiPath(
        `workflow-types/${workflowTypeId}/signatures/pending/${pendingOverrideId}`,
      ),
    )
  }

  /**
   * Reviewer-only. Removes the caller's own vote on the pending so they can
   * re-cast or leave it unvoted. Proposer's auto-approve is structural and
   * cannot be withdrawn.
   */
  async withdrawVote(
    workflowTypeId: string,
    pendingOverrideId: string,
  ): Promise<void> {
    await this.instance.delete(
      this.getApiPath(
        `workflow-types/${workflowTypeId}/signatures/pending/${pendingOverrideId}/votes/mine`,
      ),
    )
  }

  /**
   * Returns one page of audit-log entries for a workflow's signature override
   * value transitions. All filters are optional; results are newest first.
   * Undefined filter properties are dropped from the query string by axios's
   * default param serializer, so the backend sees them as "no filter".
   */
  async getHistory(
    workflowTypeId: string,
    filters: SignatureHistoryFilters = {},
  ): Promise<SignatureHistoryPageDto> {
    const { data } = await this.instance.get<SignatureHistoryPageDto>(
      this.getApiPath(`workflow-types/${workflowTypeId}/signatures/history`),
      { params: filters },
    )
    return data
  }
}
