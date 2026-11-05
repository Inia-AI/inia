export interface WorkflowNode {
  id: string
  type: 'trigger' | 'action' | 'condition' | 'addStep' | 'copilot'
  position: { x: number; y: number }
  data: NodeData
  parentId?: string
  selected?: boolean
  draggable?: boolean
  selectable?: boolean
  connectable?: boolean
}

export interface NodeData {
  label: string
  description?: string
  appId?: string
  appName?: string
  appIcon?: string
  eventType?: string
  isConfigured: boolean
  configuration?: Record<string, any>
  status?: 'idle' | 'running' | 'success' | 'error' | 'warning'
  stepNumber?: number
  isTrigger?: boolean
  isInstant?: boolean
  parentId?: string
  isPremium?: boolean
  triggerId?: string
  actionId?: string
  config?: Record<string, any>
  afterNodeId?: string // For add step buttons to track which node they come after
}

export interface WorkflowEdge {
  id: string
  source: string
  target: string
  type?: 'default' | 'conditional'
  animated?: boolean
  style?: Record<string, any>
  data?: {
    condition?: string
    label?: string
  }
}

export interface WorkflowApp {
  id: string
  name: string
  description: string
  icon: string
  category: 'apps' | 'zapier' | 'builtin' | 'ai'
  color?: string
  isPremium?: boolean
  triggers?: AppTrigger[]
  actions?: AppAction[]
  searchKeywords?: string[]
}

export interface AppTrigger {
  id: string
  name: string
  description: string
  isInstant?: boolean
  fields: FieldDefinition[]
  outputSchema?: Record<string, any>
}

export interface AppAction {
  id: string
  name: string
  description: string
  fields: FieldDefinition[]
  inputSchema?: Record<string, any>
  outputSchema?: Record<string, any>
}

export interface FieldDefinition {
  key: string
  label: string
  type: 'text' | 'number' | 'select' | 'multiselect' | 'boolean' | 'datetime' | 'file' | 'dynamic'
  required?: boolean
  placeholder?: string
  helpText?: string
  defaultValue?: any
  options?: FieldOption[]
  validation?: {
    min?: number
    max?: number
    pattern?: string
    message?: string
  }
  dependsOn?: string[]
  isDynamic?: boolean
}

export interface FieldOption {
  label: string
  value: string | number
  description?: string
}

export interface WorkflowExecution {
  id: string
  workflowId: string
  status: 'pending' | 'running' | 'completed' | 'failed' | 'paused'
  startedAt: Date
  completedAt?: Date
  steps: ExecutionStep[]
  error?: {
    message: string
    stepId: string
    details?: any
  }
  testMode?: boolean
}

export interface ExecutionStep {
  id: string
  nodeId: string
  status: 'pending' | 'running' | 'completed' | 'failed' | 'skipped'
  startedAt?: Date
  completedAt?: Date
  input?: Record<string, any>
  output?: Record<string, any>
  error?: string
  logs?: string[]
}

export interface WorkflowDraft {
  id: string
  name: string
  description?: string
  nodes: WorkflowNode[]
  edges: WorkflowEdge[]
  variables?: Record<string, any>
  settings?: WorkflowSettings
  createdAt: Date
  updatedAt: Date
  version: number
  isPublished: boolean
  lastPublishedAt?: Date
}

export interface WorkflowSettings {
  errorHandling?: 'stop' | 'continue' | 'retry'
  retryAttempts?: number
  retryDelay?: number
  timeout?: number
  timezone?: string
  notifications?: {
    onSuccess?: boolean
    onError?: boolean
    email?: string
  }
}

export interface DragDropData {
  type: 'app' | 'trigger' | 'action'
  appId?: string
  triggerId?: string
  actionId?: string
  appData?: WorkflowApp
}

export interface StepConfiguration {
  nodeId: string
  fields: Record<string, any>
  authentication?: {
    connected: boolean
    accountName?: string
    accountId?: string
  }
  mappings?: FieldMapping[]
  testData?: Record<string, any>
}

export interface FieldMapping {
  sourceField: string
  targetField: string
  sourceNodeId?: string
  transformation?: 'none' | 'toString' | 'toNumber' | 'toBoolean' | 'toArray' | 'custom'
  customTransformation?: string
}

export interface CopilotSuggestion {
  id: string
  type: 'next-step' | 'fix-error' | 'optimization' | 'template'
  title: string
  description: string
  confidence: number
  action?: {
    type: 'add-node' | 'configure-node' | 'add-connection' | 'apply-template'
    data: any
  }
}

export interface WorkflowTemplate {
  id: string
  name: string
  description: string
  category: string
  icon?: string
  apps: string[]
  nodes: WorkflowNode[]
  edges: WorkflowEdge[]
  popularity?: number
  estimatedTime?: string
  difficulty?: 'easy' | 'medium' | 'hard'
}

export type NodeStatus = 'idle' | 'configuring' | 'configured' | 'error' | 'running' | 'success'

export interface ZoomLevel {
  value: number
  label: string
}

export const ZOOM_LEVELS: ZoomLevel[] = [
  { value: 0.5, label: '50%' },
  { value: 0.75, label: '75%' },
  { value: 0.9, label: '90%' },
  { value: 1, label: '100%' },
  { value: 1.1, label: '110%' },
  { value: 1.25, label: '125%' },
  { value: 1.5, label: '150%' },
  { value: 2, label: '200%' },
]