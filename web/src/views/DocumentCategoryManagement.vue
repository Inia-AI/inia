<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import { DocumentCategoryForm } from '@/components/organisms'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { useToast } from '@/components/ui/toast/use-toast'
import API from '@/api'
import type { DocumentCategory, CreateDocumentCategoryRequest, UpdateDocumentCategoryRequest } from '@/api/documentCategories/schema'
import { DIALOG_TYPE } from '@/enums'
import { IniaIcon } from '@/components/atoms'

const { t: $t } = useI18n()
const { toast } = useToast()

const showDialog = ref(false)
const dialogType = ref<string>('')
const selectedCategory = ref<DocumentCategory | null>(null)

const { data, isLoading, refetch } = API.DocumentCategories.useGetDocumentCategories()
const { mutateAsync: createCategory } = API.DocumentCategories.useCreateDocumentCategory()
const { mutateAsync: updateCategory } = API.DocumentCategories.useUpdateDocumentCategory()
const { mutateAsync: deleteCategory } = API.DocumentCategories.useDeleteDocumentCategory()

const categories = computed(() => {
  const cats = data.value || []
  // Sort by displayOrder
  return [...cats].sort((a, b) => a.displayOrder - b.displayOrder)
})

const editLabel = (category: DocumentCategory) =>
  $t('page.documentCategoryManagement.rowActions.edit', { category: category.id })

const deleteLabel = (category: DocumentCategory) =>
  $t('page.documentCategoryManagement.rowActions.delete', { category: category.id })

const onCreate = () => {
  dialogType.value = DIALOG_TYPE.ADD
  selectedCategory.value = null
  showDialog.value = true
}

const onEdit = (category: DocumentCategory) => {
  dialogType.value = DIALOG_TYPE.EDIT
  selectedCategory.value = category
  showDialog.value = true
}

const onDelete = (category: DocumentCategory) => {
  dialogType.value = DIALOG_TYPE.DELETE
  selectedCategory.value = category
  showDialog.value = true
}

const categoryInitialValues = computed(() => {
  if (dialogType.value === DIALOG_TYPE.EDIT && selectedCategory.value) {
    return {
      name: selectedCategory.value.name,
      displayOrder: selectedCategory.value.displayOrder
    }
  }
  return null
})

const resetState = () => {
  dialogType.value = ''
  selectedCategory.value = null
  showDialog.value = false
}

const handleSubmit = async (formData: CreateDocumentCategoryRequest | UpdateDocumentCategoryRequest) => {
  try {
    if (dialogType.value === DIALOG_TYPE.ADD) {
      await createCategory(formData as CreateDocumentCategoryRequest)
      toast({
        class: 'text-sm',
        description: `Successfully created document category "${(formData as CreateDocumentCategoryRequest).name}"`,
        duration: 2000,
      })
    } else if (dialogType.value === DIALOG_TYPE.EDIT && selectedCategory.value) {
      await updateCategory({ id: selectedCategory.value.id, data: formData as UpdateDocumentCategoryRequest })
      toast({
        class: 'text-sm',
        description: `Successfully updated document category`,
        duration: 2000,
      })
    }
    resetState()
    refetch()
  } catch {
    toast({
      class: 'text-sm',
      description: 'Error saving document category',
      duration: 2000,
      variant: 'destructive',
    })
  }
}

const handleDelete = async () => {
  if (!selectedCategory.value) return

  try {
    await deleteCategory(selectedCategory.value.id)
    toast({
      class: 'text-sm',
      description: `Successfully deleted document category "${selectedCategory.value.name}"`,
      duration: 2000,
    })
    resetState()
    refetch()
  } catch {
    toast({
      class: 'text-sm',
      description: 'Error deleting document category',
      duration: 2000,
      variant: 'destructive',
    })
  }
}

const dialogTitle = computed(() => {
  switch (dialogType.value) {
    case DIALOG_TYPE.ADD:
      return $t('page.documentCategoryManagement.dialog.createTitle')
    case DIALOG_TYPE.EDIT:
      return $t('page.documentCategoryManagement.dialog.editTitle')
    case DIALOG_TYPE.DELETE:
      return $t('page.documentCategoryManagement.dialog.deleteTitle')
    default:
      return ''
  }
})
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div>
        <h1 class="page-title">{{ $t('page.documentCategoryManagement.title') }}</h1>
        <p class="page-subtitle">
          {{ $t('page.documentCategoryManagement.subtitle') }}
        </p>
      </div>
      <Button class="btn-gradient-primary" @click="onCreate">
        {{ $t('page.documentCategoryManagement.createLabel') }}
      </Button>
    </div>

    <div class="card-panel table-container">
      <table v-if="categories && categories.length" class="data-table">
        <thead>
          <tr>
            <th>{{ $t('page.documentCategoryManagement.table.id') }}</th>
            <th>{{ $t('page.documentCategoryManagement.table.name') }}</th>
            <th>{{ $t('page.documentCategoryManagement.table.displayOrder') }}</th>
            <th class="actions-column">{{ $t('page.documentCategoryManagement.table.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="category in categories" :key="category.id">
            <td class="font-mono text-sm text-muted-foreground">{{ category.id }}</td>
            <td>{{ category.name }}</td>
            <td class="text-center">{{ category.displayOrder }}</td>
            <td class="actions-column">
              <div class="action-buttons">
                <Button
                  variant="ghost"
                  size="sm"
                  :title="editLabel(category)"
                  :aria-label="editLabel(category)"
                  @click="onEdit(category)"
                >
                  <IniaIcon name="pencil" class="w-4 h-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  :title="deleteLabel(category)"
                  :aria-label="deleteLabel(category)"
                  @click="onDelete(category)"
                >
                  <IniaIcon name="trash" class="w-4 h-4 text-error" />
                </Button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!isLoading" class="empty-state">
        {{ $t('page.documentCategoryManagement.emptyText') }}
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <Dialog v-model:open="showDialog">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ dialogTitle }}</DialogTitle>
        </DialogHeader>

        <div v-if="dialogType === DIALOG_TYPE.DELETE">
          <p>
            {{ $t('page.documentCategoryManagement.dialog.deleteConfirmation') }}
            <strong>{{ selectedCategory?.name }}</strong>?
          </p>

          <div class="flex justify-end gap-4 mt-4">
            <Button variant="outline" @click="resetState">
              {{ $t('page.documentCategoryManagement.form.cancelLabel') }}
            </Button>
            <Button type="submit" variant="destructive" @click="handleDelete">
              {{ $t('page.documentCategoryManagement.form.deleteLabel') }}
            </Button>
          </div>
        </div>

        <DocumentCategoryForm
          v-else
          :initial-values="categoryInitialValues"
          @submit="handleSubmit"
          @cancel="resetState"
        />
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
.data-table {
  width: 100%;
  border-collapse: collapse;
}

.data-table th,
.data-table td {
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--stroke);
}

.data-table th {
  font-weight: 600;
  color: var(--text-secondary);
  background-color: var(--bg-1);
}

.data-table tbody tr:hover {
  background-color: var(--bg-1);
}

.actions-column {
  width: 100px;
  text-align: center;
}

.action-buttons {
  display: flex;
  justify-content: center;
  gap: 0.25rem;
}
</style>
