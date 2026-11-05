import { BaseService } from '../base'
import type { ServiceParams } from '../schema'
import type {
  ExecutionBlocks,
  SignaturePromptTestResult,
  TestSignaturePromptRequest,
} from './types'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  /**
   * Load every AI Query block inside a past workflow execution snapshot, plus the
   * resolved signature values used at execution time. Populates the playground's
   * block/field picker.
   */
  async getExecutionBlocks(workflowExecutionId: string): Promise<ExecutionBlocks> {
    const { data } = await this.instance.get<ExecutionBlocks>(
      this.getApiPath(`workflow-executions/${workflowExecutionId}/blocks`),
    )
    return data
  }

  /**
   * Run the signature test-replay. The backend swaps the captured signature value
   * for `testValue` inside the captured prompts and calls the AI provider once.
   * Response body is the new output — no prompt strings, by design.
   */
  async testSignaturePrompt(
    workflowExecutionId: string,
    body: TestSignaturePromptRequest,
  ): Promise<SignaturePromptTestResult> {
    const { data } = await this.instance.post<SignaturePromptTestResult>(
      this.getApiPath(`workflow-executions/${workflowExecutionId}/test-signature-prompt`),
      body,
    )
    return data
  }
}
