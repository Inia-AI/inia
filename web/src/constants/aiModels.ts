/**
 * AI Model Definitions
 *
 * Shared model constants for the workflow editor.
 * These match the C# backend enum definitions (ActiveEnumEntry = true only).
 * Model IDs are AWS Bedrock format used by the platform.
 *
 * TODO: Load model list and defaults from API instead of hardcoding.
 * This is tracked in the backend TenantModelFamilyResolver which determines
 * the default model per tenant.
 */

export interface AiModel {
  value: string
  label: string
}

// Default model used when no override is specified
// TODO: This should come from API based on tenant configuration
export const DEFAULT_MODEL_ID = 'us.anthropic.claude-sonnet-4-5-20250929-v1:0'
export const DEFAULT_MODEL_DISPLAY_NAME = 'Claude 4.5 Sonnet'

// Anthropic Models - AWS Bedrock format
export const ANTHROPIC_MODELS: AiModel[] = [
  // Haiku Series
  { value: 'us.anthropic.claude-3-haiku-20240307-v1:0', label: 'AWS Bedrock US Haiku 3' },
  { value: 'us.anthropic.claude-haiku-4-5-20251001-v1:0', label: 'AWS Bedrock US Haiku 4.5' },
  // Sonnet Series
  { value: 'us.anthropic.claude-sonnet-4-5-20250929-v1:0', label: 'AWS Bedrock US Sonnet 4.5' },
  // Opus Series
  { value: 'us.anthropic.claude-opus-4-1-20250805-v1:0', label: 'AWS Bedrock US Opus 4.1' },
  { value: 'us.anthropic.claude-opus-4-5-20251101-v1:0', label: 'AWS Bedrock US Opus 4.5' },
]

// Mistral Models - AWS Bedrock format
export const MISTRAL_MODELS: AiModel[] = [
  { value: 'mistral.mistral-large-2407-v1:0', label: 'AWS Mistral Large 2 (24.07)' },
  { value: 'us.mistral.pixtral-large-2502-v1:0', label: 'AWS US Pixtral Large (25.02)' },
]

// Model display name mappings - Normalizes model IDs to friendly names
// Maps normalized model IDs (without regional/provider prefixes and version suffixes) to display names
export const MODEL_DISPLAY_NAMES: Record<string, string> = {
  // Anthropic Haiku Series
  'claude-3-haiku': 'Claude 3 Haiku',
  'claude-3-5-haiku': 'Claude 3.5 Haiku',
  'claude-haiku-4-5': 'Claude 4.5 Haiku',

  // Anthropic Sonnet Series
  'claude-3-sonnet': 'Claude 3 Sonnet',
  'claude-3-5-sonnet': 'Claude 3.5 Sonnet',
  'claude-3-7-sonnet': 'Claude 3.7 Sonnet',
  'claude-sonnet-4': 'Claude 4 Sonnet',
  'claude-sonnet-4-5': 'Claude 4.5 Sonnet',

  // Anthropic Opus Series
  'claude-3-opus': 'Claude 3 Opus',
  'claude-opus-4': 'Claude 4 Opus',
  'claude-opus-4-1': 'Claude 4.1 Opus',
  'claude-opus-4-5': 'Claude 4.5 Opus',

  // Mistral Models
  'mistral-7b-instruct': 'Mistral 7B',
  'mixtral-8x7b-instruct': 'Mixtral 8x7B',
  'mistral-large-2402': 'Mistral Large (24.02)',
  'mistral-large-2407': 'Mistral Large 2 (24.07)',
  'pixtral-large-2502': 'Pixtral Large (25.02)',
}

/**
 * Normalizes a model ID to extract the core model name.
 * Handles: public API, AWS Bedrock, and regional variations.
 *
 * Examples:
 * - us.anthropic.claude-3-haiku-20240307-v1:0 → claude-3-haiku
 * - claude-3-5-sonnet-20241022 → claude-3-5-sonnet
 * - eu.anthropic.claude-opus-4-5-20251101-v1:0 → claude-opus-4-5
 */
export function normalizeModelId(modelId: string): string {
  // Remove regional prefixes: us., eu., global.
  let normalized = modelId.replace(/^(us|eu|global)\./, '')

  // Remove provider prefix: anthropic., mistral.
  normalized = normalized.replace(/^(anthropic|mistral)\./, '')

  // Remove AWS Bedrock version suffix: -v1:0, -v2:0, etc.
  normalized = normalized.replace(/-v\d+:\d+$/, '')

  // Remove date suffixes (e.g., -20240307, -20241022) to get base model name
  // But keep version numbers like -4-5 or -3-7
  const datePattern = /-\d{8}$/
  if (datePattern.test(normalized)) {
    normalized = normalized.replace(datePattern, '')
  }

  return normalized
}

/**
 * Detects the provider from a model ID.
 * Handles AWS Bedrock, public API, and regional variations.
 */
export function detectModelProvider(modelId: string): 'anthropic' | 'mistral' | 'unknown' {
  // Check exact match in model lists
  if (ANTHROPIC_MODELS.some(m => m.value === modelId)) return 'anthropic'
  if (MISTRAL_MODELS.some(m => m.value === modelId)) return 'mistral'

  // Check for AWS Bedrock patterns
  if (modelId.includes('anthropic.claude')) return 'anthropic'
  if (modelId.includes('mistral.')) return 'mistral'

  // Check for public API patterns
  if (modelId.startsWith('claude')) return 'anthropic'
  if (modelId.startsWith('mistral') || modelId.startsWith('mixtral') || modelId.startsWith('pixtral')) return 'mistral'

  return 'unknown'
}

/**
 * Gets the display name for a model ID.
 * Uses normalization to handle all model ID variations.
 * Returns the default model name when no override is set.
 */
export function getModelDisplayName(modelId: string): string {
  // TODO: When no override, the default model should come from API
  if (!modelId) return `${DEFAULT_MODEL_DISPLAY_NAME} (Default)`

  const normalizedId = normalizeModelId(modelId)
  return MODEL_DISPLAY_NAMES[normalizedId] || normalizedId
}
