import { WorkflowVariableTypes } from '@/enums'

/**
 * Lightweight workflow type option for dropdown filters.
 * Used by WorkflowReports and other views that need workflow type list.
 */
export interface WorkflowTypeOption {
  workflowTypeId: string
  workflowTypeName: string
}

// Block types for workflow editor - matches C# BlockTypes enum from IniaAi.Platform
export enum BlockType {
  // ========================================
  // IMPLEMENTED IN BACKEND (C# BlockTypes)
  // ========================================

  // Core AI & Processing
  AiQuery = 'AiQuery',
  SimpleRag = 'SimpleRag',
  Agent = 'Agent',

  // Search & Web
  WebSnippetSearch = 'WebSnippetSearch',
  IntelligentSearch = 'IntelligentSearch',
  DataApiCall = 'DataApiCall',
  WebApiCall = 'WebApiCall',

  // Data Transformation
  Merge = 'Merge',
  CalculateNumber = 'CalculateNumber',
  SwitchCase = 'SwitchCase',

  // Financial Blocks
  ExtractFinancialMetric = 'ExtractFinancialMetric',
  BuildTopicsForAllDocuments = 'BuildTopicsForAllDocuments',
  CreatePeerAnalysis = 'CreatePeerAnalysis',
  ExchangeRate = 'ExchangeRate',

  // Visualization
  DynamicChart = 'DynamicChart',
  Json2Xlsx = 'Json2Xlsx',

  // Control Flow (Basic)
  LoopStart = 'LoopStart',
  LoopEnd = 'LoopEnd',

  // ========================================
  // FUTURE/PLANNED (Not yet in C# backend)
  // Advanced control flow for complex workflows
  // ========================================
  WhileLoop = 'WhileLoop',
  DoWhileLoop = 'DoWhileLoop',
  ForLoop = 'ForLoop',
  ParallelFork = 'ParallelFork',
  ParallelJoin = 'ParallelJoin',
  Switch = 'Switch',
  Break = 'Break',
  Continue = 'Continue',

  // ========================================
  // LEGACY (Keep for backward compatibility)
  // ========================================

  DocumentProcessing = 'DocumentProcessing',
  VariableInput = 'VariableInput',
  Conditional = 'Conditional',
  DataTransform = 'DataTransform',
  Output = 'Output',
}

export interface WorkflowVariable {
  id: string
  replacementTag: string
  displayName: string
  description?: string
  type: WorkflowVariableTypes
  isRequired?: boolean
  isHidden?: boolean
  isConfidential?: boolean
  defaultValue?: string
  options?: Record<string, string>
}

export interface DocumentClass {
  key: string
  displayName: string
  description: string
  requirement: string // "Required" | "Suggested" | "Optional"
  shouldBeSingular: boolean
  isOutputDocument: boolean
  documentCategoryId?: string
  /** The workflow and the output that make a file of this input class; absent when no workflow makes it. */
  madeBy?: DocumentClassMadeBy | null
}

export interface DocumentClassMadeBy {
  workflowTypeId: string
  workflowTypeName: string
  outputName: string
}

export interface Workflow {
  id: string
  workflowTypeId: string
  workflowTypeName: string
  isAvailable: boolean
  documentClassKeys?: string[]
  documentClasses?: DocumentClass[]
  variables: WorkflowVariable[]
  counterpartyTypeMappings?: string[]
  isCounterpartyRequired?: boolean
  supportsPeerCompanies?: boolean
  /** True when the platform reads each document and decides its class, so choosing one is optional. */
  classifiesDocuments?: boolean
  /** On a type the platform classifies, the required classes it cannot recognise, which the user must choose. */
  classesToChoose?: string[]
}

// Workflow Editor interfaces
export interface TextInput {
  id: string
  blockName: string
  replacementTag: string
  value: string
  executionState?: string
}

export interface ExecutionState {
  // Type discriminator (matches BlockType enum)
  type?: string

  // Nested properties object (typed configuration data)
  // Will be populated once strategies are updated
  properties?: object

  // Common execution state fields
  status?: string
  error?: string
  startedAt?: string
  completedAt?: string
  startTime?: string
  endTime?: string
  executionDuration?: string
  outputTitle?: string
  isExecuting?: boolean

  // Legacy text inputs (maintained for backward compatibility)
  textInputs?: Record<string, string>

  // LEGACY: Flat properties (kept for backward compatibility)
  // These are maintained during migration - will be deprecated once strategies populate properties
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

  // Block-specific execution state properties (legacy)
  result?: any // Generic result for blocks that output a single value
  extractedValues?: Record<string, string> // Legacy - for backward compatibility
  formulaUsed?: string // For CalculateNumber block
  pageCount?: number // Legacy - for backward compatibility
  pagesExtracted?: string // Legacy - for backward compatibility
  pageIndices?: string // Legacy - for backward compatibility
  exchangeRate?: number // For ExchangeRate block
  fromCurrency?: string // For ExchangeRate block - resolved from currency
  toCurrency?: string // For ExchangeRate block - resolved to currency
  url?: string // For web/API blocks - resolved URL
  entityName?: string // For search blocks - resolved entity name
  jsonInput?: string // For Json2Xlsx block - resolved JSON input
}

// RAG settings execution state (resolved placeholders after execution)
export interface RagExecutionState {
  exactIndexesInput?: string
  numberOfPagesInput?: string
  startIndexInput?: string
  endIndexInput?: string
  pageAdjustmentInput?: string
  tableOfContentsInput?: string
  topicsQuery?: string
  queryTemplate?: string
  iterationLimit?: string
  contextLimit?: string
  keywords?: string[]
  shouldFilterKeyWordsExclusively?: boolean
  shouldSearchOverviews?: boolean
  shouldIncludeNeighboringPages?: boolean
  smartFilterExpressions?: string[]
}

// Base RAG settings interface
export interface BaseRagSettings {
  id?: string
  type: string
  executionState?: RagExecutionState | null
}

// Discriminated union types for RAG settings
export interface WholeDocumentRagSettings extends BaseRagSettings {
  type: 'WholeDocument'
}

export interface TopNPagesRagSettings extends BaseRagSettings {
  type: 'TopNPages'
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
  smartFilterExpressions?: string[]
}

export interface TopNPagesWithNeighborsRagSettings extends BaseRagSettings {
  type: 'TopNPagesPerFilterWithTwoNeighbors'
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
  smartFilterExpressions?: string[]
}

export interface TopPageRagSettings extends BaseRagSettings {
  type: 'TopPage'
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
  smartFilterExpressions?: string[]
}

export interface TopPageWithFollowingRagSettings extends BaseRagSettings {
  type: 'TopPageWithNFollowingPages'
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
  smartFilterExpressions?: string[]
}

export interface NPagesFromIndexRagSettings extends BaseRagSettings {
  type: 'NPagesStartingFromIndex'
  pageAdjustmentInput?: string
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
}

export interface KeywordBasedRagSettings extends BaseRagSettings {
  type: 'KeywordBased'
  startIndexInput?: string
  endIndexInput?: string
  numberOfPagesInput?: string
  keywords?: string[]
  shouldFilterKeyWordsExclusively?: boolean
  shouldSearchOverviews?: boolean
  shouldIncludeNeighboringPages?: boolean
  smartFilterExpressions?: string[]
}

export interface ExactPagesRagSettings extends BaseRagSettings {
  type: 'ExactPages'
  pageAdjustmentInput?: string
  exactIndexesInput?: string
}

export interface UseTableOfContentsRagSettings extends BaseRagSettings {
  type: 'UseTableOfContents'
  pageAdjustmentInput?: string
  tableOfContentsInput?: string
}

export interface UseTopicsRagSettings extends BaseRagSettings {
  type: 'UseTopics'
  topicsQuery?: string
}

export interface UseAutoDetectedTocRagSettings extends BaseRagSettings {
  type: 'UseAutoDetectedTableOfContents'
  tableOfContentsInput?: string
  topicsQuery?: string
}

export interface RagAgentRagSettings extends BaseRagSettings {
  type: 'RagAgent'
  queryTemplate?: string
  iterationLimit?: string
  contextLimit?: string
}

// Union type for all RAG settings
export type RagSettings =
  | WholeDocumentRagSettings
  | TopNPagesRagSettings
  | TopNPagesWithNeighborsRagSettings
  | TopPageRagSettings
  | TopPageWithFollowingRagSettings
  | NPagesFromIndexRagSettings
  | KeywordBasedRagSettings
  | ExactPagesRagSettings
  | UseTableOfContentsRagSettings
  | UseTopicsRagSettings
  | UseAutoDetectedTocRagSettings
  | RagAgentRagSettings

export interface WebApiCallProperties {
  urlTemplate?: string
  requestPayloadTemplate?: string
  httpMethod: string
  contentType: string
  acceptHeader: string
  additionalHeaders: Record<string, string>
  delayAfterRequestSeconds?: number
}

export interface Block {
  name: string // Unique identifier (used as primary key at API level)
  replacementTag?: string
  description: string
  type: BlockType
  order: number
  isEnabled: boolean
  textInputs: TextInput[]

  // UI-only properties (not sent from backend)
  userPromptTemplatePreview?: string
  isSelected?: boolean
  isExpanded?: boolean

  // Output configuration properties
  isOutputSource?: boolean
  outputTitleTemplate?: string

  // RAG configuration (top-level properties)
  documentClasses?: string[]
  supportedDocumentClasses?: string[]

  // Regression testing schema (JSON, travels with block template clones)
  regressionSchema?: string | null

  // Block-specific configuration properties (sent from backend as BlockDataDto)
  // Backend uses composition pattern with BlockDataDto polymorphic hierarchy
  properties?: {
    // Execution state for properties - contains resolved values after placeholder substitution
    executionState?: {
      // Resolved template values (same structure as templates but with placeholders replaced)
      userPromptTemplate?: string
      systemPromptTemplate?: string
      searchTermTemplate?: string
      searchTextTemplate?: string
      formulaTemplate?: string
      textTemplate?: string
      htmlTemplate?: string
      loopedListTemplate?: string
      urlTemplate?: string
      requestPayloadTemplate?: string
      conditionalTagTemplate?: string
      extractionTags?: string[]
      pageIndicesTemplate?: string
      // AiQuery specific - RAG input context
      inputContext?: string
      // WebSnippetSearch specific
      entityName?: string
      // ExchangeRate specific - resolved currency values
      fromCurrency?: string
      toCurrency?: string
      // Json2Xlsx specific - resolved JSON input
      jsonInput?: string
      jsonInputTemplate?: string
    } | null

    // AiQuery specific properties
    userPromptTemplate?: string
    systemPromptTemplate?: string
    maxTokens?: number
    shouldUseAdvancedModel?: boolean
    modelIdOverride?: string
    shouldChunkLargeInputContexts?: boolean
    chunkSize?: number
    chunkingMode?: 'FixedPageCount' | 'OptimalCharacters'
    maxChunkCharacters?: number
    preservePageOrder?: boolean
    shouldUsePageImages?: boolean
    shouldUseOverviews?: boolean
    shouldUseThinkingMode?: boolean
    thinkingBudget?: number
    shouldUseCachingForInputContext?: boolean
    conditionalTagTemplate?: string

    // WebApiCall and DataTransform properties
    textTemplate?: string
    urlTemplate?: string
    searchTermTemplate?: string

    // WebSnippetSearch specific properties
    searchTextTemplate?: string
    entityName?: string

    // CalculateNumber specific properties
    formulaTemplate?: string

    // ExchangeRate specific properties
    fromCurrency?: string
    toCurrency?: string
    exchangeDate?: string
    useCurrentDate?: boolean
    operationMode?: 'GetRate' | 'Convert'
    inputValue?: string

    // IntelligentSearch (Exa) specific properties
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

    // Legacy properties (kept for backward compatibility)
    extractionTags?: string[]
    pageIndicesTemplate?: string
    htmlTemplate?: string

    // LoopStart specific properties
    listTitle?: string
    loopedListTemplate?: string

    // CreatePeerAnalysis specific properties
    analysisTemplate?: string
    peerCompanies?: string[]
    targetUnit?: string
    targetCurrency?: string

    // WebApiCall specific properties
    requestPayloadTemplate?: string
    httpMethod?: string
    contentType?: string
    acceptHeader?: string
    additionalHeaders?: Record<string, string>
    delayAfterRequestSeconds?: number

    // Json2Xlsx specific properties
    conversionType?: string
    jsonInputTemplate?: string
    outputFileName?: string
    mappingJson?: string

    // LEGACY/PLANNED BLOCKS (Not yet in C# backend)

    // Conditional block properties
    conditionLabel?: string
    conditionType?: string
    leftOperand?: string
    operator?: string
    rightOperand?: string
    rightOperandType?: string
    variableToCheck?: string
    expression?: string
    logicOperator?: string
    conditions?: any[]
    truePathBlocks?: any[]
    falsePathBlocks?: any[]

    // DoWhileLoop/ForLoop/WhileLoop properties
    loopLabel?: string
    condition?: string
    maxIterations?: number
    loopVariable?: string
    loopBodyBlocks?: any[]
    initialValue?: string
    increment?: string

    // DynamicChart properties
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

    // Document creation property (for blocks that can create output documents)
    createsDocumentClass?: string

    // Output block properties
    outputName?: string
    outputType?: string
    displayFormat?: string
    exportFormat?: string
    filename?: string
    emailSubject?: string
    tableName?: string
    dbOperation?: string
    variableName?: string
    variableScope?: string
    dataTransform?: string
    includeMetadata?: boolean
    compressOutput?: boolean
    encryptOutput?: boolean
    timestampOutput?: boolean
    displayContent?: string
    dataSource?: string
    dataSourceType?: string
    emailRecipients?: string | string[]
    webhookUrl?: string
    outputFormat?: string
    outputTemplate?: string
    downloadEnabled?: boolean
    customStyles?: string

    // ParallelFork/Join properties
    forkLabel?: string
    executionStrategy?: string
    joinLabel?: string
    joinTimeout?: number
    incomingBranches?: Array<{
      id: string
      label?: string
      status?: string
      condition?: string
      targetBlockId?: string
    }>
    branches?: any[]
    joinCondition?: string
    joinStrategy?: string

    // Switch properties (control flow - PLANNED, not yet implemented)
    switchExpression?: string
    switchFlowCases?: any[]

    // SwitchCase properties (content selection)
    cases?: Record<string, string>
    defaultValue?: string

    // DataTransform properties
    transformationType?: string
    mappings?: Record<string, string>

    // VariableInput properties
    description?: string
    acceptedTypes?: string[]
    inputType?: string
    placeholder?: string
    rows?: number
    required?: boolean
    selectOptions?: Array<{ value: string; label: string }>
    minLength?: number
    maxLength?: number
    minValue?: number
    maxValue?: number
    step?: number
    pattern?: string
    checkboxLabel?: string
    filePrompt?: string

    // BuildTopicsForAllDocuments properties
    maxTopics?: number

    // Legacy properties (kept for backward compatibility)
    financialAnalysisToHtmlTypes?: string[]

    // DocumentProcessing properties
    getOverviews?: boolean

    // ExtractFinancialMetric properties
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

  // Execution state properties
  executionState?: ExecutionState
  blockResponse?: any
  executionResults?: Record<string, any>

  // RAG settings
  ragSettings?: RagSettings[]

  // Control flow properties
  controlFlow?: {
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

  // Other block type properties can be added as needed
  children?: Block[]
}

export interface WorkflowSection {
  name: string // Unique identifier (used as primary key at API level)
  title?: string // Display name (can be duplicate, e.g., "Processing")
  description: string
  order: number
  isEnabled: boolean
  blocks: Block[] // Matches API structure (was 'children')
}

// Report layout configuration for Word/PDF export
export interface ReportLayout {
  orientation: string // 'Portrait' or 'Landscape'
  pageSize: string // 'A4', 'Letter', etc.
  version: string
}

// Workflow configuration including report layout
export interface WorkflowConfiguration {
  gridLayout?: {
    mode: string
    columns: number
    elements?: Array<{
      blockOrder: number
      row: number
      column: number
      columnSpan: number
      rowSpan: number
    }>
  }
  reportLayout?: ReportLayout
  customSettings?: Record<string, unknown>
  version: string
}

// ========================================
// CITATION TYPES
// ========================================

/**
 * Base interface for citation sources.
 * Uses discriminated union pattern with 'type' field.
 */
export interface BaseCitationSource {
  type: 'Document' | 'Url' | 'Block'
  citedText: string
}

/**
 * Citation source from an uploaded document.
 */
export interface DocumentCitationSource extends BaseCitationSource {
  type: 'Document'
  pageId: string
  pageNumber: number
  filePageNumber?: number
  documentName?: string
  /** The cited text could not be located in the document's pages — shown as unchecked, never as confirmed. */
  unverified?: boolean
}

/**
 * Citation source from a web URL.
 */
export interface UrlCitationSource extends BaseCitationSource {
  type: 'Url'
  url: string
}

/**
 * Citation source from a previous workflow block.
 * Contains upstreamSources for tracing the provenance chain.
 */
export interface BlockCitationSource extends BaseCitationSource {
  type: 'Block'
  blockId: string
  blockTitle?: string
  upstreamSources: CitationSource[]
  /** The pages and links from the block's list whose excerpt states this citation's fact. */
  matchingSources?: CitationSource[]
}

/**
 * Every page and link that one block, or any block upstream of it, cited during the run.
 */
export interface BlockCitedSources {
  blockId: string
  sources: CitationSource[]
}

/**
 * Union type for all citation source types.
 */
export type CitationSource =
  | DocumentCitationSource
  | UrlCitationSource
  | BlockCitationSource

/**
 * Represents a citation with its sources.
 * Links to fact markers in content via the id property.
 */
export interface Citation {
  id: string
  sources: CitationSource[]
}

/**
 * Type guard to check if a source is a DocumentCitationSource
 */
export function isDocumentSource(
  source: CitationSource,
): source is DocumentCitationSource {
  return source.type === 'Document'
}

/**
 * Type guard to check if a source is a UrlCitationSource
 */
export function isUrlSource(
  source: CitationSource,
): source is UrlCitationSource {
  return source.type === 'Url'
}

/**
 * Type guard to check if a source is a BlockCitationSource
 */
export function isBlockSource(
  source: CitationSource,
): source is BlockCitationSource {
  return source.type === 'Block'
}
