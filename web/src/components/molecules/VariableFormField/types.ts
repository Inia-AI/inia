import type { Component } from 'vue'
import type { WorkflowVariableTypes } from '@/enums'

export interface VariableFieldDefinition {
  component: Component
  componentProps?: Record<string, unknown>
  name: string
  label: string
  type: WorkflowVariableTypes
  description?: string
  isRequired?: boolean
}
