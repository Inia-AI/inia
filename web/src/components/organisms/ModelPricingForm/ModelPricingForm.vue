<script lang="ts" setup>
import { computed, PropType, ref, watch } from 'vue'
import type { AcceptableValue } from 'reka-ui'
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'

import type {
  ModelPricing,
  ModelPricingComponentRequest,
  PricingType,
} from '@/api/modelPricing/schema'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

// Component types - API returns string names, but we need to send numbers
const ComponentTypeStrings = {
  Input: 'Input',
  Output: 'Output',
  CacheWrite5m: 'CacheWrite5m',
  CacheWrite1h: 'CacheWrite1h',
  CacheRead: 'CacheRead',
  Embedding: 'Embedding',
} as const

// Numeric values for API requests
const ComponentTypeNumbers = {
  Input: 0,
  Output: 1,
  CacheWrite5m: 20,
  CacheWrite1h: 21,
  CacheRead: 22,
  Embedding: 40,
} as const

// Unit enum values for API requests
const PricingUnit = {
  PerMillion: 0,
} as const

const $emits = defineEmits(['submit', 'cancel'])

const props = defineProps({
  initialValues: {
    type: Object as PropType<ModelPricing | null>,
    required: false,
  },
})

interface FormData {
  provider: string
  family: string
  modelId: string
  modelAlias: string
  displayName: string
  pricingType: PricingType
  inputTokenCostPerMillion?: number
  outputTokenCostPerMillion?: number
  cacheWrite5mCostPerMillion?: number
  cacheWrite1hCostPerMillion?: number
  cacheReadCostPerMillion?: number
  embeddingCostPerMillion?: number
  effectiveFrom: string
  effectiveTo?: string
  isActive: boolean
}

const defaultValues: FormData = {
  provider: '',
  family: '',
  modelId: '',
  modelAlias: '',
  displayName: '',
  pricingType: 'Llm',
  inputTokenCostPerMillion: undefined,
  outputTokenCostPerMillion: undefined,
  cacheWrite5mCostPerMillion: undefined,
  cacheWrite1hCostPerMillion: undefined,
  cacheReadCostPerMillion: undefined,
  embeddingCostPerMillion: undefined,
  effectiveFrom: new Date().toISOString().slice(0, 16),
  effectiveTo: undefined,
  isActive: true,
}

// Convert components array to flat form fields
// API returns componentType as string (e.g., "Input", "Output")
const componentsToFormData = (components: ModelPricing['components']): Partial<FormData> => {
  const result: Partial<FormData> = {}

  for (const comp of components || []) {
    // Compare against string values since API returns strings
    switch (comp.componentType) {
      case ComponentTypeStrings.Input:
        result.inputTokenCostPerMillion = comp.cost
        break
      case ComponentTypeStrings.Output:
        result.outputTokenCostPerMillion = comp.cost
        break
      case ComponentTypeStrings.CacheWrite5m:
        result.cacheWrite5mCostPerMillion = comp.cost
        break
      case ComponentTypeStrings.CacheWrite1h:
        result.cacheWrite1hCostPerMillion = comp.cost
        break
      case ComponentTypeStrings.CacheRead:
        result.cacheReadCostPerMillion = comp.cost
        break
      case ComponentTypeStrings.Embedding:
        result.embeddingCostPerMillion = comp.cost
        break
    }
  }

  return result
}

// Convert flat form fields to components array
// API expects numeric componentType values for requests
const formDataToComponents = (data: FormData): ModelPricingComponentRequest[] => {
  const components: ModelPricingComponentRequest[] = []

  if (data.pricingType === 'Llm') {
    if (data.inputTokenCostPerMillion !== undefined && data.inputTokenCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.Input,
        cost: data.inputTokenCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
    if (data.outputTokenCostPerMillion !== undefined && data.outputTokenCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.Output,
        cost: data.outputTokenCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
    if (data.cacheWrite5mCostPerMillion !== undefined && data.cacheWrite5mCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.CacheWrite5m,
        cost: data.cacheWrite5mCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
    if (data.cacheWrite1hCostPerMillion !== undefined && data.cacheWrite1hCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.CacheWrite1h,
        cost: data.cacheWrite1hCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
    if (data.cacheReadCostPerMillion !== undefined && data.cacheReadCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.CacheRead,
        cost: data.cacheReadCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
  } else {
    if (data.embeddingCostPerMillion !== undefined && data.embeddingCostPerMillion !== null) {
      components.push({
        componentType: ComponentTypeNumbers.Embedding,
        cost: data.embeddingCostPerMillion,
        unit: PricingUnit.PerMillion,
        currency: 'USD',
      })
    }
  }

  return components
}

const formData = ref<FormData>({
  ...defaultValues,
})

const isLlmType = computed(() => formData.value.pricingType === 'Llm')
const isEmbeddingType = computed(() => formData.value.pricingType === 'Embedding')

const schema = computed(() =>
  z.object({
    provider: z.string().min(1, 'Provider is required').max(100),
    family: z.string().max(50).optional().nullable(),
    modelId: z.string().min(1, 'Model ID is required').max(200),
    modelAlias: z.string().max(200).optional().nullable(),
    displayName: z.string().min(1, 'Display name is required').max(250),
    pricingType: z.enum(['Llm', 'Embedding']),
    inputTokenCostPerMillion: z.number().min(0).optional().nullable(),
    outputTokenCostPerMillion: z.number().min(0).optional().nullable(),
    cacheWrite5mCostPerMillion: z.number().min(0).optional().nullable(),
    cacheWrite1hCostPerMillion: z.number().min(0).optional().nullable(),
    cacheReadCostPerMillion: z.number().min(0).optional().nullable(),
    embeddingCostPerMillion: z.number().min(0).optional().nullable(),
    effectiveFrom: z.string().min(1, 'Effective from date is required'),
    effectiveTo: z.string().optional().nullable(),
    isActive: z.boolean(),
  }),
)

const form = useForm<FormData>({
  validationSchema: schema,
  initialValues: formData.value,
})

const handleSubmit = form.handleSubmit(data => {
  // Convert form data to API format with components array
  const apiData = {
    provider: data.provider,
    family: data.family || undefined,
    modelId: data.modelId,
    modelAlias: data.modelAlias || undefined,
    displayName: data.displayName,
    pricingType: data.pricingType,
    effectiveFrom: data.effectiveFrom,
    effectiveTo: data.effectiveTo || undefined,
    isActive: data.isActive,
    components: formDataToComponents(data),
  }
  $emits('submit', apiData)
})

const handlePricingTypeChange = (value: AcceptableValue) => {
  formData.value.pricingType = value as PricingType
  // Clear fields for the other type
  if (value === 'Llm') {
    formData.value.embeddingCostPerMillion = undefined
  } else {
    formData.value.inputTokenCostPerMillion = undefined
    formData.value.outputTokenCostPerMillion = undefined
    formData.value.cacheWrite5mCostPerMillion = undefined
    formData.value.cacheWrite1hCostPerMillion = undefined
    formData.value.cacheReadCostPerMillion = undefined
  }
}

watch(
  () => props.initialValues,
  newValues => {
    if (newValues) {
      // Convert components to flat form fields
      const componentFields = componentsToFormData(newValues.components)
      Object.assign(formData.value, {
        ...defaultValues,
        provider: newValues.provider || '',
        family: newValues.family || '',
        modelId: newValues.modelId || '',
        modelAlias: newValues.modelAlias || '',
        displayName: newValues.displayName || '',
        pricingType: newValues.pricingType || 'Llm',
        effectiveFrom: newValues.effectiveFrom?.slice(0, 16) || defaultValues.effectiveFrom,
        effectiveTo: newValues.effectiveTo?.slice(0, 16) || undefined,
        isActive: newValues.isActive ?? true,
        ...componentFields,
      })
    } else {
      Object.assign(formData.value, { ...defaultValues })
    }
    form.resetForm({ values: formData.value })
  },
  { immediate: true },
)
</script>

<template>
  <form @submit="handleSubmit" class="flex flex-col gap-4 max-h-[70vh] overflow-y-auto pr-2">
    <!-- Basic Information -->
    <div class="grid grid-cols-2 gap-4">
      <FormField v-slot="{ componentField }" name="provider">
        <FormItem>
          <FormLabel>Provider</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.provider"
              placeholder="e.g., anthropic-api, aws-bedrock"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="family">
        <FormItem>
          <FormLabel>Family (optional)</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.family"
              placeholder="e.g., Anthropic, Mistral, Cohere"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>
    </div>

    <div class="grid grid-cols-2 gap-4">
      <FormField v-slot="{ componentField }" name="modelId">
        <FormItem>
          <FormLabel>Model ID</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.modelId"
              placeholder="e.g., claude-sonnet-4-20250514"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="modelAlias">
        <FormItem>
          <FormLabel>Model Alias (optional)</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.modelAlias"
              placeholder="Alternative model name for matching"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>
    </div>

    <FormField v-slot="{ componentField }" name="displayName">
      <FormItem>
        <FormLabel>Display Name</FormLabel>
        <FormControl>
          <Input
            v-bind="componentField"
            v-model="formData.displayName"
            placeholder="Human-readable name"
          />
        </FormControl>
        <FormMessage />
      </FormItem>
    </FormField>

    <!-- Pricing Type -->
    <FormField v-slot="{ componentField }" name="pricingType">
      <FormItem>
        <FormLabel>Pricing Type</FormLabel>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -- the Select root renders no element; FormControl hands its id to the SelectTrigger, which the FormLabel names -->
        <Select
          :model-value="formData.pricingType"
          @update:model-value="handlePricingTypeChange"
        >
          <FormControl>
            <SelectTrigger v-bind="componentField">
              <SelectValue placeholder="Select pricing type" />
            </SelectTrigger>
          </FormControl>
          <SelectContent>
            <SelectItem value="Llm">LLM (Language Model)</SelectItem>
            <SelectItem value="Embedding">Embedding</SelectItem>
          </SelectContent>
        </Select>
        <FormMessage />
      </FormItem>
    </FormField>

    <!-- LLM Pricing Fields -->
    <div v-if="isLlmType" class="space-y-4 p-4 border rounded-lg bg-muted/30">
      <h4 class="font-medium text-sm">LLM Token Costs (USD per million tokens)</h4>

      <div class="grid grid-cols-2 gap-4">
        <FormField v-slot="{ componentField }" name="inputTokenCostPerMillion">
          <FormItem>
            <FormLabel>Input Token Cost</FormLabel>
            <FormControl>
              <Input
                v-bind="componentField"
                v-model.number="formData.inputTokenCostPerMillion"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g., 3.00"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="outputTokenCostPerMillion">
          <FormItem>
            <FormLabel>Output Token Cost</FormLabel>
            <FormControl>
              <Input
                v-bind="componentField"
                v-model.number="formData.outputTokenCostPerMillion"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g., 15.00"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>
      </div>

      <h4 class="font-medium text-sm mt-4">Cache Costs (optional)</h4>

      <div class="grid grid-cols-3 gap-4">
        <FormField v-slot="{ componentField }" name="cacheWrite5mCostPerMillion">
          <FormItem>
            <FormLabel>Cache Write (5min TTL)</FormLabel>
            <FormControl>
              <Input
                v-bind="componentField"
                v-model.number="formData.cacheWrite5mCostPerMillion"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g., 3.75"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="cacheWrite1hCostPerMillion">
          <FormItem>
            <FormLabel>Cache Write (1hr TTL)</FormLabel>
            <FormControl>
              <Input
                v-bind="componentField"
                v-model.number="formData.cacheWrite1hCostPerMillion"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g., 4.50"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>

        <FormField v-slot="{ componentField }" name="cacheReadCostPerMillion">
          <FormItem>
            <FormLabel>Cache Read</FormLabel>
            <FormControl>
              <Input
                v-bind="componentField"
                v-model.number="formData.cacheReadCostPerMillion"
                type="number"
                step="0.01"
                min="0"
                placeholder="e.g., 0.30"
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        </FormField>
      </div>
    </div>

    <!-- Embedding Pricing Fields -->
    <div v-if="isEmbeddingType" class="space-y-4 p-4 border rounded-lg bg-muted/30">
      <h4 class="font-medium text-sm">Embedding Cost (USD per million tokens)</h4>

      <FormField v-slot="{ componentField }" name="embeddingCostPerMillion">
        <FormItem>
          <FormLabel>Embedding Cost</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model.number="formData.embeddingCostPerMillion"
              type="number"
              step="0.01"
              min="0"
              placeholder="e.g., 0.10"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>
    </div>

    <!-- Effective Dates -->
    <div class="grid grid-cols-2 gap-4">
      <FormField v-slot="{ componentField }" name="effectiveFrom">
        <FormItem>
          <FormLabel>Effective From</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.effectiveFrom"
              type="datetime-local"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <FormField v-slot="{ componentField }" name="effectiveTo">
        <FormItem>
          <FormLabel>Effective To (optional)</FormLabel>
          <FormControl>
            <Input
              v-bind="componentField"
              v-model="formData.effectiveTo"
              type="datetime-local"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>
    </div>

    <!-- Active Status -->
    <FormField v-slot="{ componentField }" name="isActive">
      <FormItem>
        <div class="flex items-center space-x-2">
          <FormControl>
            <Checkbox
              :id="componentField.name"
              :model-value="formData.isActive"
              @update:model-value="
                (val) => {
                  const checked = val === true
                  formData.isActive = checked
                  componentField['onUpdate:modelValue']?.(checked)
                }
              "
            />
          </FormControl>
          <FormLabel for="isActive" class="mt-0!">Active</FormLabel>
        </div>
        <FormMessage />
      </FormItem>
    </FormField>

    <!-- Actions -->
    <div class="flex justify-end gap-4 mt-4 pt-4 border-t">
      <Button variant="outline" type="button" @click="$emit('cancel')">
        Cancel
      </Button>
      <Button type="submit" variant="soft">Submit</Button>
    </div>
  </form>
</template>
