/**
 * Strongly typed block definitions matching backend BlockDataDto hierarchy
 *
 * Architecture:
 * - Each block type has specific properties interface
 * - Discriminated union enables type narrowing
 * - All nested properties (textInputs, ragSettings, properties) tracked for:
 *   - Validation
 *   - Undo/Redo
 *   - Change detection
 */

import type { RagSettings, TextInput, BlockType as BlockTypeEnum } from './schema'

// ============================================
// BASE INTERFACES
// ============================================

/**
 * Common properties shared by all blocks
 */
export interface BaseBlock {
  name: string
  replacementTag?: string
  description: string
  order: number
  isEnabled: boolean

  // Nested structures that need tracking
  textInputs: TextInput[]
  ragSettings?: RagSettings[]
  supportedDocumentClasses?: string[]

  // Output configuration
  outputTitleTemplate?: string
  isOutputSource?: boolean
  shouldDisplayInLogs?: boolean

  // Execution state (separate from block definition)
  executionState?: ExecutionState
  blockResponse?: any
  executionResults?: Record<string, any>

  // UI-only properties (not tracked for undo/redo)
  userPromptTemplatePreview?: string
  isSelected?: boolean
  isExpanded?: boolean

  // Control flow (for advanced blocks)
  controlFlow?: ControlFlowConfig

  // Children (for nested blocks)
  children?: Block[]
}

export interface ExecutionState {
  type?: string
  properties?: object
  status?: string
  error?: string
  startedAt?: string
  completedAt?: string
  startTime?: string
  endTime?: string
  executionDuration?: string
  outputTitle?: string
  isExecuting?: boolean
  textInputs?: Record<string, string>

  // Legacy properties for backward compatibility
  userPrompt?: string
  systemPrompt?: string
  inputContext?: string
  text?: string
  searchTerm?: string
  loopedList?: string
  templatePrompt?: string
  conditionalTag?: string
  extractionTags?: string[]
  sources?: string[]
  confidenceScore?: number
  rating?: number
  result?: any
  extractedValues?: Record<string, string>
  formulaUsed?: string
  pageCount?: number
  pagesExtracted?: string
  exchangeRate?: number
}

export interface ControlFlowConfig {
  type?: 'conditional' | 'loop' | 'parallel' | 'switch'
  conditionExpression?: string
  maxIterations?: number
  loopVariable?: string
  trueBranchId?: string
  falseBranchId?: string
  nextBlockIds?: string[]
  joinPointId?: string
  branches?: Array<{
    id: string
    label?: string
    status?: string
    condition?: string
    targetBlockId: string
  }>
}

// ============================================
// BLOCK-SPECIFIC PROPERTY INTERFACES
// ============================================

/**
 * Chunking mode for page batching
 * Backend: ChunkingMode enum
 */
export type ChunkingMode = 'FixedPageCount' | 'OptimalCharacters'

/**
 * AiQuery block properties
 * Backend: AiQueryDataDto
 */
export interface AiQueryProperties {
  userPromptTemplate: string
  systemPromptTemplate?: string
  maxTokens: number
  shouldUseAdvancedModel?: boolean
  modelIdOverride?: string
  shouldChunkLargeInputContexts?: boolean
  chunkSize?: number
  chunkingMode?: ChunkingMode
  maxChunkCharacters?: number
  preservePageOrder?: boolean
  shouldUsePageImages?: boolean
  shouldUseOverviews?: boolean
  shouldUseThinkingMode?: boolean
  thinkingBudget?: number
  shouldUseCachingForInputContext?: boolean
  conditionalTagTemplate?: string
}

/**
 * SimpleRag block properties
 * Backend: RetrieveDataFromDocumentsDataDto
 */
export interface SimpleRagProperties {
  textTemplate?: string
  getOverviews?: boolean
}

/**
 * WebApiCall block properties
 * Backend: WebApiCallDataDto
 */
export interface WebApiCallProperties {
  urlTemplate: string
  requestPayloadTemplate?: string
  httpMethod: 'GET' | 'POST'
  contentType: string
  acceptHeader: string
  additionalHeaders?: Record<string, string>
  delayAfterRequestSeconds?: number
}

/**
 * DataApiCall block properties
 * Backend: DataApiCallDataDto
 */
export interface DataApiCallProperties {
  urlTemplate: string
  searchTermTemplate?: string
}

/**
 * WebSnippetSearch block properties
 * Backend: SnippetSearchDataDto
 */
export interface WebSnippetSearchProperties {
  searchTextTemplate: string
  entityName?: string
  createsDocumentClass?: string
}

/**
 * IntelligentSearch (Exa) block properties
 * Backend: IntelligentSearchDataDto
 */
export interface IntelligentSearchProperties {
  searchTermTemplate: string
  searchType?: string
  numResults?: number
  textEnabled?: boolean
  includeHtmlTags?: boolean
  maxCharacters?: number
  highlightsEnabled?: boolean
  summaryEnabled?: boolean
  summaryQuery?: string
  summarySchema?: string
  excludeDomains?: string[]
  includeDomains?: string[]
  category?: string
  startPublishedDate?: string
  endPublishedDate?: string
  liveCrawlStrategy?: string
  liveCrawlTimeout?: number
  subpages?: number
  subpageTarget?: string[]
  createsDocumentClass?: string
}


/**
 * Merge block properties
 * Backend: MergeDataDto
 */
export interface MergeProperties {
  textTemplate: string
}


/**
 * CalculateNumber block properties
 * Backend: CalculateFormulaDataDto
 */
export interface CalculateNumberProperties {
  formulaTemplate: string
}


/**
 * ExchangeRate operation mode
 * Backend: ExchangeRateOperationMode enum
 */
export type ExchangeRateOperationMode = 'GetRate' | 'Convert'

/**
 * ExchangeRate block properties
 * Backend: ExchangeRateDataDto
 */
export interface ExchangeRateProperties {
  fromCurrency: string
  toCurrency: string
  exchangeDate?: string
  useCurrentDate?: boolean
  operationMode?: ExchangeRateOperationMode
  inputValue?: string
}

/**
 * LoopStart block properties
 * Backend: LoopStartDataDto
 */
export interface LoopStartProperties {
  listTitle?: string
  loopedListTemplate: string
}

/**
 * LoopEnd block properties
 * Backend: LoopEndDataDto (no properties)
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type
export interface LoopEndProperties {
  // Empty - LoopEnd has no properties
}


/**
 * ExtractFinancialMetric block properties
 * Backend: ExtractFinancialMetricDataDto
 */
export interface ExtractFinancialMetricProperties {
  userPromptTemplate: string
  incomeStatementMappings?: string
  incomeStatementLvl2?: string
  incomeStatementLvl3?: string
  bsAssets1?: string
  bsAssets2?: string
  bsAssets3?: string
  bsLiabilities1?: string
  bsLiabilities2?: string
  bsLiabilities3?: string
  bsEquity1?: string
  bsEquity2?: string
  bsEquity3?: string
  cfOperating1?: string
  cfOperating2?: string
  cfOperating3?: string
  cfInvesting1?: string
  cfInvesting2?: string
  cfInvesting3?: string
  cfFinancing1?: string
  cfFinancing2?: string
  cfFinancing3?: string
  cfCashReconciliation1?: string
  cfCashReconciliation2?: string
  cfCashReconciliation3?: string
}

/**
 * BuildTopicsForAllDocuments block properties
 * Backend: BuildTopicsDataDto
 */
export interface BuildTopicsForAllDocumentsProperties {
  maxTopics?: number
  topicsModelTemplate?: string
}


/**
 * CreatePeerAnalysis block properties
 * Backend: CreatePeerAnalysisDataDto
 */
export interface CreatePeerAnalysisProperties {
  analysisTemplate?: string
  peerCompanies?: string[]
  targetUnit?: string
  targetCurrency?: string
}

/**
 * DynamicChart block properties
 * Backend: DynamicChartDataDto
 */
export interface DynamicChartProperties {
  dataSourceJson?: string
  chartType?: string
  chartTitle?: string
  xAxisField?: string
  yAxisField?: string
  xAxisLabel?: string
  yAxisLabel?: string
  seriesGroupField?: string
  colorScheme?: string
  stacking?: string
  chartHeight?: number
  chartWidth?: number
  showLegend?: boolean
  showGridLines?: boolean
  enableAnimations?: boolean
  dataFilters?: string
}

/**
 * Json2Xlsx block properties
 * Backend: Json2XlsxDataDto
 */
export interface Json2XlsxProperties {
  conversionType?: string
  jsonInputTemplate?: string
  createsDocumentClass?: string
  outputFileName?: string
}

// ============================================
// STRONGLY TYPED BLOCK DEFINITIONS
// ============================================

export interface AiQueryBlock extends BaseBlock {
  type: 'AiQuery'
  properties: AiQueryProperties
}

export interface SimpleRagBlock extends BaseBlock {
  type: 'SimpleRag'
  properties: SimpleRagProperties
}

export interface WebApiCallBlock extends BaseBlock {
  type: 'WebApiCall'
  properties: WebApiCallProperties
}

export interface DataApiCallBlock extends BaseBlock {
  type: 'DataApiCall'
  properties: DataApiCallProperties
}

export interface WebSnippetSearchBlock extends BaseBlock {
  type: 'WebSnippetSearch'
  properties: WebSnippetSearchProperties
}

export interface IntelligentSearchBlock extends BaseBlock {
  type: 'IntelligentSearch'
  properties: IntelligentSearchProperties
}

export interface MergeBlock extends BaseBlock {
  type: 'Merge'
  properties: MergeProperties
}

export interface CalculateNumberBlock extends BaseBlock {
  type: 'CalculateNumber'
  properties: CalculateNumberProperties
}

export interface ExchangeRateBlock extends BaseBlock {
  type: 'ExchangeRate'
  properties: ExchangeRateProperties
}

export interface LoopStartBlock extends BaseBlock {
  type: 'LoopStart'
  properties: LoopStartProperties
}

export interface LoopEndBlock extends BaseBlock {
  type: 'LoopEnd'
  properties: LoopEndProperties
}

export interface ExtractFinancialMetricBlock extends BaseBlock {
  type: 'ExtractFinancialMetric'
  properties: ExtractFinancialMetricProperties
}

export interface BuildTopicsForAllDocumentsBlock extends BaseBlock {
  type: 'BuildTopicsForAllDocuments'
  properties: BuildTopicsForAllDocumentsProperties
}

export interface CreatePeerAnalysisBlock extends BaseBlock {
  type: 'CreatePeerAnalysis'
  properties: CreatePeerAnalysisProperties
}

export interface DynamicChartBlock extends BaseBlock {
  type: 'DynamicChart'
  properties: DynamicChartProperties
}

export interface Json2XlsxBlock extends BaseBlock {
  type: 'Json2Xlsx'
  properties: Json2XlsxProperties
}

// ============================================
// DISCRIMINATED UNION
// ============================================

/**
 * Discriminated union of all block types
 * TypeScript uses the 'type' property to narrow the type
 *
 * Usage:
 * ```typescript
 * function handleBlock(block: Block) {
 *   if (block.type === 'AiQuery') {
 *     // TypeScript knows block is AiQueryBlock
 *     console.log(block.properties.userPromptTemplate)
 *   }
 * }
 * ```
 */
export type Block =
  // Core AI & Processing
  | AiQueryBlock
  | SimpleRagBlock

  // Search & Web
  | WebSnippetSearchBlock
  | IntelligentSearchBlock
  | DataApiCallBlock
  | WebApiCallBlock

  // Data Transformation
  | MergeBlock
  | CalculateNumberBlock

  // Financial Blocks
  | ExtractFinancialMetricBlock
  | BuildTopicsForAllDocumentsBlock
  | CreatePeerAnalysisBlock
  | ExchangeRateBlock

  // Visualization
  | DynamicChartBlock
  | Json2XlsxBlock

  // Control Flow
  | LoopStartBlock
  | LoopEndBlock

/**
 * Extract properties type from a block type
 * Usage: BlockProperties<'AiQuery'> => AiQueryProperties
 */
export type BlockProperties<T extends BlockTypeEnum> = Extract<Block, { type: T }>['properties']

/**
 * Extract full block type from block type enum
 * Usage: BlockByType<'AiQuery'> => AiQueryBlock
 */
export type BlockByType<T extends BlockTypeEnum> = Extract<Block, { type: T }>

/**
 * Helper to check if a block is of a specific type
 */
export function isBlockType<T extends BlockTypeEnum>(
  block: Block,
  type: T
): block is BlockByType<T> {
  return block.type === type
}
