/**
 * Pricing type for model pricing entries
 */
export type PricingType = 'Llm' | 'Embedding'

/**
 * Model pricing list item (returned by list endpoint)
 */
export interface ModelPricingListItem {
  id: string
  provider: string
  family?: string
  modelId: string
  displayName: string
  pricingType: PricingType
  isActive: boolean
}

/**
 * Pricing component for model pricing (normalized structure)
 * Note: API returns componentType as string (e.g., "Input", "Output")
 */
export interface ModelPricingComponent {
  id?: string
  componentType: string
  cost: number
  unit: number | string
  currency: string
}

/**
 * Full model pricing details (returned by get/create/update endpoints)
 */
export interface ModelPricing {
  id: string
  provider: string
  family?: string
  modelId: string
  modelAlias?: string
  displayName: string
  pricingType: PricingType
  effectiveFrom: string
  effectiveTo?: string
  isActive: boolean
  components: ModelPricingComponent[]
}

/**
 * Request payload for a pricing component
 */
export interface ModelPricingComponentRequest {
  componentType: number
  cost: number
  unit: number
  currency: string
}

/**
 * Request payload for creating a model pricing entry
 */
export interface CreateModelPricingRequest {
  provider: string
  family?: string
  modelId: string
  modelAlias?: string
  displayName: string
  pricingType: PricingType
  effectiveFrom: string
  effectiveTo?: string
  isActive: boolean
  components: ModelPricingComponentRequest[]
}

/**
 * Request payload for updating a model pricing entry
 */
export interface UpdateModelPricingRequest {
  provider: string
  family?: string
  modelId: string
  modelAlias?: string
  displayName: string
  pricingType: PricingType
  effectiveFrom: string
  effectiveTo?: string
  isActive: boolean
  components: ModelPricingComponentRequest[]
}
