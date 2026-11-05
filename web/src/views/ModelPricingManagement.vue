<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import { IniaSelect } from '@/components/molecules'
import { DataTable } from '@/components/organisms'
import ModelPricingForm from '@/components/organisms/ModelPricingForm/ModelPricingForm.vue'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { useModelPricingTable } from '@/tables'
import { useToast } from '@/components/ui/toast/use-toast'
import API from '@/api'
import type { ODataQueryParams } from '@/api/schema'
import type { ModelPricing, ModelPricingListItem } from '@/api/modelPricing/schema'
import { OptionType } from '@/types'
import { DIALOG_TYPE } from '@/enums'
import { escapeODataValue } from '@/utils/odata'

const { t: $t } = useI18n()
const { toast } = useToast()


const page = ref(0)
const pageSize = ref(10)
const orderBy = ref('provider asc, modelId asc')
const providerFilter = ref<string>('')
const familyFilter = ref<string>('')
const pricingTypeFilter = ref<string>('')
const showDialog = ref(false)
const dialogType = ref<string>('')
const selectedPricing = ref<ModelPricing | null>(null)

const { mutateAsync: createModelPricing } = API.ModelPricing.useCreateModelPricing()
const { mutateAsync: getModelPricing } = API.ModelPricing.useGetModelPricing()
const { mutateAsync: updateModelPricing } = API.ModelPricing.useUpdateModelPricing()
const { mutateAsync: deleteModelPricing } = API.ModelPricing.useDeleteModelPricing()

const queryParams = computed<ODataQueryParams>(() => {
  const params: ODataQueryParams = {
    $skip: page.value * pageSize.value,
    $top: pageSize.value,
    $count: true,
    $orderby: orderBy.value,
  }

  const filters: string[] = []

  if (providerFilter.value?.trim()) {
    filters.push(`provider eq '${escapeODataValue(providerFilter.value)}'`)
  }

  if (familyFilter.value?.trim()) {
    filters.push(`family eq '${escapeODataValue(familyFilter.value)}'`)
  }

  if (pricingTypeFilter.value?.trim()) {
    filters.push(`pricingType eq '${escapeODataValue(pricingTypeFilter.value)}'`)
  }

  if (filters.length > 0) {
    params.$filter = filters.join(' and ')
  }

  return params
})

const { data, isLoading } = API.ModelPricing.useGetModelPricings(queryParams)

const modelPricings = computed(() => data.value?.value || [])
const totalCount = computed(() => data.value?.count || 0)

// Extract unique providers for filter dropdown
const providerOptions = computed<OptionType[]>(() => {
  const providers = new Set<string>()
  modelPricings.value.forEach(p => providers.add(p.provider))
  const options: OptionType[] = Array.from(providers)
    .sort()
    .map(p => ({ label: p, value: p }))
  options.unshift({ label: 'All Providers', value: '' })
  return options
})

// Extract unique families for filter dropdown
const familyOptions = computed<OptionType[]>(() => {
  const families = new Set<string>()
  modelPricings.value.forEach(p => {
    if (p.family) families.add(p.family)
  })
  const options: OptionType[] = Array.from(families)
    .sort()
    .map(f => ({ label: f, value: f }))
  options.unshift({ label: 'All Families', value: '' })
  return options
})

const pricingTypeOptions = computed<OptionType[]>(() => [
  { label: 'All Types', value: '' },
  { label: 'LLM', value: 'Llm' },
  { label: 'Embedding', value: 'Embedding' },
])

const onCreate = () => {
  dialogType.value = DIALOG_TYPE.ADD
  selectedPricing.value = null
  showDialog.value = true
}

const onPageChange = (newPage: number) => {
  page.value = newPage
}

const onOrderByChange = (newOrderBy: string) => {
  orderBy.value = newOrderBy
  page.value = 0
}

const pricingInitialValues = computed(() => {
  if (dialogType.value === DIALOG_TYPE.EDIT && selectedPricing.value) {
    return {
      ...selectedPricing.value,
      effectiveFrom: selectedPricing.value.effectiveFrom?.slice(0, 16) || '',
      effectiveTo: selectedPricing.value.effectiveTo?.slice(0, 16) || '',
    }
  }
  return null
})

const resetState = () => {
  dialogType.value = ''
  selectedPricing.value = null
  showDialog.value = false
}

const handleSubmit = async (formData: any) => {
  try {
    switch (dialogType.value) {
      case DIALOG_TYPE.ADD:
        await createModelPricing(formData)
        toast({
          class: 'text-sm',
          description: `Successfully added model pricing for ${formData.displayName}`,
          duration: 2000,
        })
        break

      case DIALOG_TYPE.EDIT:
        if (selectedPricing.value?.id) {
          await updateModelPricing({
            id: selectedPricing.value.id,
            params: formData,
          })
          toast({
            class: 'text-sm',
            description: `Successfully updated model pricing for ${formData.displayName}`,
            duration: 2000,
          })
        }
        break
    }
    resetState()
  } catch (error: any) {
    toast({
      class: 'text-sm',
      description: error?.response?.data?.detail || 'Error saving model pricing',
      duration: 3000,
      variant: 'destructive',
    })
  }
}

const handleDelete = async () => {
  try {
    if (selectedPricing.value?.id) {
      await deleteModelPricing(selectedPricing.value.id)
      toast({
        class: 'text-sm',
        description: `Successfully deleted model pricing for ${selectedPricing.value.displayName}`,
        duration: 2000,
      })
    }
    resetState()
  } catch (error: any) {
    toast({
      class: 'text-sm',
      description: error?.response?.data?.detail || 'Error deleting model pricing',
      duration: 3000,
      variant: 'destructive',
    })
  }
}

const onEdit = async (row: { original: ModelPricingListItem }) => {
  try {
    const details = await getModelPricing(row.original.id)
    selectedPricing.value = details
    dialogType.value = DIALOG_TYPE.EDIT
    showDialog.value = true
  } catch {
    toast({
      class: 'text-sm',
      description: 'Error loading model pricing details',
      duration: 2000,
      variant: 'destructive',
    })
  }
}

const onDelete = (row: { original: ModelPricingListItem }) => {
  selectedPricing.value = row.original as unknown as ModelPricing
  dialogType.value = DIALOG_TYPE.DELETE
  showDialog.value = true
}

const { columns } = useModelPricingTable({
  onEdit,
  onDelete,
})

const dialogTitle = computed(() => {
  switch (dialogType.value) {
    case DIALOG_TYPE.ADD:
      return $t('page.modelPricingManagement.dialog.createTitle')
    case DIALOG_TYPE.EDIT:
      return $t('page.modelPricingManagement.dialog.editTitle')
    case DIALOG_TYPE.DELETE:
      return $t('page.modelPricingManagement.dialog.deleteTitle')
    default:
      return ''
  }
})
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div>
        <h1 class="page-title">{{ $t('page.modelPricingManagement.title') }}</h1>
        <p class="page-subtitle">
          {{ $t('page.modelPricingManagement.subtitle') }}
        </p>
      </div>
      <Button class="btn-gradient-primary" @click="onCreate">
        {{ $t('page.modelPricingManagement.createLabel') }}
      </Button>
    </div>

    <div class="filter-section">
      <div class="filter-container">
        <IniaSelect
          v-model="providerFilter"
          :aria-label="$t('page.modelPricingManagement.filters.provider')"
          :options="providerOptions"
          placeholder="Filter by Provider"
        />
      </div>
      <div class="filter-container">
        <IniaSelect
          v-model="familyFilter"
          :aria-label="$t('page.modelPricingManagement.filters.family')"
          :options="familyOptions"
          placeholder="Filter by Family"
        />
      </div>
      <div class="filter-container">
        <IniaSelect
          v-model="pricingTypeFilter"
          :aria-label="$t('page.modelPricingManagement.filters.pricingType')"
          :options="pricingTypeOptions"
          placeholder="Filter by Type"
        />
      </div>
    </div>

    <div class="card-panel table-container">
      <DataTable
        v-if="modelPricings && modelPricings.length"
        :columns="columns"
        :data="modelPricings"
        :page-size="pageSize"
        :total-count="totalCount"
        :page="page"
        :order-by="orderBy"
        @update:page="onPageChange"
        @update:order-by="onOrderByChange"
      />
      <div v-else-if="!isLoading" class="empty-state">
        {{ $t('page.modelPricingManagement.emptyText') }}
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <Dialog v-model:open="showDialog">
      <DialogContent class="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{{ dialogTitle }}</DialogTitle>
        </DialogHeader>

        <div v-if="dialogType === DIALOG_TYPE.DELETE">
          <p>
            {{ $t('page.modelPricingManagement.dialog.deleteConfirm') }}
            <strong>{{ selectedPricing?.displayName }}</strong>?
          </p>

          <div class="flex justify-end gap-4 mt-4">
            <Button variant="outline" @click="resetState">
              {{ $t('page.modelPricingManagement.form.cancelLabel') }}
            </Button>
            <Button variant="destructive" @click="handleDelete">
              {{ $t('page.modelPricingManagement.form.deleteLabel') }}
            </Button>
          </div>
        </div>

        <ModelPricingForm
          v-else
          :initial-values="pricingInitialValues"
          @submit="handleSubmit"
          @cancel="resetState"
        />
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
.filter-section {
  display: flex;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-xl);
}

.filter-container {
  max-width: 200px;
  width: 100%;
}
</style>
