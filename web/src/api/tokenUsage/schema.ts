// Usage component type enum
export type PricingComponentType =
  | 'Input'
  | 'Output'
  | 'CacheRead'
  | 'CacheWrite5m'
  | 'CacheWrite1h'
  | 'Embedding'
  | 'Reasoning'
  | 'ImageInput'
  | 'VideoInput'
  | 'AudioInput'

// Token usage component
export interface TokenUsageComponent {
  componentType: PricingComponentType
  units: number
  cost: number
}

// Token usage record for OData listing
export interface TokenUsage {
  id: string
  organizationId: string
  userId: string | null
  userDisplayName: string | null
  workflowTypeId: string | null
  workflowTypeName: string | null
  workflowExecutionId: string | null
  blockId: string | null
  documentId: string | null
  usageType: string
  executionContext: string
  blockType: string | null
  provider: string
  modelId: string
  components: TokenUsageComponent[]
  totalCost: number
  createdAt: string
  description: string | null
}

// Period summary (Today, This Week, This Month, All Time)
export interface TokenUsageSummary {
  period: string
  periodStart: string
  periodEnd: string
  totalInputTokens: number
  totalOutputTokens: number
  totalTokens: number
  totalCacheCreationTokens: number
  totalCacheReadTokens: number
  totalCost: number
  operationCount: number
}

// Usage breakdown by type
export interface UsageByType {
  usageType: string
  displayName: string
  totalTokens: number
  totalCost: number
  percentage: number
  operationCount: number
}

// Personal usage summary
export interface MyUsageSummary {
  userId: string
  userDisplayName: string | null
  today: TokenUsageSummary
  thisWeek: TokenUsageSummary
  thisMonth: TokenUsageSummary
  allTime: TokenUsageSummary
  byUsageType: UsageByType[]
}

// Execution summary for workflow type
export interface ExecutionUsageSummary {
  workflowExecutionId: string
  userId: string | null
  userDisplayName: string | null
  totalTokens: number
  totalCost: number
  executedAt: string
}

// Workflow type usage summary
export interface WorkflowTypeUsageSummary {
  workflowTypeId: string
  workflowTypeName: string
  totalInputTokens: number
  totalOutputTokens: number
  totalTokens: number
  totalEmbeddingTokens: number
  totalCost: number
  executionCount: number
  averageTokensPerExecution: number
  averageCostPerExecution: number
  byUsageType: UsageByType[]
  recentExecutions: ExecutionUsageSummary[]
}

// Usage by workflow type
export interface UsageByWorkflowType {
  workflowTypeId: string
  workflowTypeName: string
  totalTokens: number
  totalCost: number
  percentage: number
  executionCount: number
}

// Usage by user
export interface UsageByUser {
  userId: string
  userDisplayName: string | null
  totalTokens: number
  totalCost: number
  percentage: number
  operationCount: number
}

// Daily trend data
export interface DailyUsageTrend {
  date: string
  totalTokens: number
  totalCost: number
  llmTokens: number
  embeddingTokens: number
  operationCount: number
}

// Organization dashboard
export interface OrganizationUsageDashboard {
  organizationId: string
  organizationName: string
  today: TokenUsageSummary
  thisWeek: TokenUsageSummary
  thisMonth: TokenUsageSummary
  allTime: TokenUsageSummary
  byUsageType: UsageByType[]
  byWorkflowType: UsageByWorkflowType[]
  byUser: UsageByUser[]
  dailyTrend: DailyUsageTrend[]
}

export interface ExecutionCostLine {
  blockId: string | null
  name: string
  replacementTag: string | null
  blockType: string | null
  models: string[]
  calls: number
  inputTokens: number
  outputTokens: number
  cacheReadTokens: number
  cacheWriteTokens: number
  cost: number
}

export interface ExecutionCost {
  executionId: string
  totalCost: number
  usageRecordCount: number
  unpricedRecordCount: number
  blocks: ExecutionCostLine[]
  other: ExecutionCostLine[]
}
