/**
 * DTOs for the signature-test-replay playground.
 *
 * Mirrors `IniaAi.WebApi.Contracts/Responses/Signatures/ExecutionBlocksDto.cs`
 * and `SignaturePromptTestResultDto.cs` (camelCase).
 *
 * IP boundary: neither DTO carries `userPrompt` or `systemPrompt`. The
 * backend deliberately strips those from the response — they are Inia IP
 * and must never reach the client. Do NOT add fields here that would
 * expose them; every future addition must be checked against that rule.
 */

/** One replayable signature field on an execution-captured AI Query block. */
export interface ReplayableSignatureField {
  placeholderName: string
  displayName: string
  description: string | null
  defaultValue: string
  /** The exact resolved value that fed the prompt at execution time. Null on
   *  pre-capture runs (executions that happened before this feature shipped);
   *  in that case `canReplay === false` and the Run button is disabled. */
  capturedResolvedValue: string | null
  canReplay: boolean
}

/** A single AI Query block inside an execution snapshot. `blockId` is the
 *  execution-clone id — stable within the snapshot — and the FE round-trips
 *  it back to the backend when firing a replay. `sectionTitle` disambiguates
 *  the block when the same step appears in multiple parameterised-section
 *  variants (same replacementTag, different context). */
export interface ExecutionBlock {
  blockId: string
  blockName: string
  sectionTitle: string | null
  replacementTag: string | null
  rawResponse: string | null
  signatureFields: ReplayableSignatureField[]
}

/** Response shape for `GET /workflow-executions/{id}/blocks`. */
export interface ExecutionBlocks {
  workflowExecutionId: string
  workflowTypeId: string | null
  blocks: ExecutionBlock[]
}

/** Body for `POST /workflow-executions/{id}/test-signature-prompt`. */
export interface TestSignaturePromptRequest {
  blockId: string
  placeholderName: string
  testValue: string
}

/** Response body for the replay POST. Deliberately carries NO prompt fields. */
export interface SignaturePromptTestResult {
  oldValueUsed: string
  newValueUsed: string
  occurrencesReplaced: number
  responseText: string
  inputTokens: number | null
  outputTokens: number | null
  modelUsed: string | null
}
