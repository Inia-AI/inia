<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { IniaIcon, IniaLoader } from '@/components/atoms'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { useToast } from '@/components/ui/toast/use-toast'
import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type { CounterpartyType } from '@/api/counterpartyTypes/schema'

const NAME_MAX_LENGTH = 250
const DESCRIPTION_MAX_LENGTH = 1000

const { t: $t } = useI18n()
const { toast } = useToast()

const params = ref({ $top: 10000, $orderby: 'name asc', $count: true })
const { data, isLoading } = API.CounterpartyTypes.useGetCounterpartyTypes(params)
const types = computed<CounterpartyType[]>(() => data.value?.value ?? [])

const { mutateAsync: createType, isPending: isCreating } = API.CounterpartyTypes.useCreateCounterpartyType()
const { mutateAsync: updateType, isPending: isUpdating } = API.CounterpartyTypes.useUpdateCounterpartyType()
const { mutateAsync: deleteType, isPending: isDeleting } = API.CounterpartyTypes.useDeleteCounterpartyType()

const formOpen = ref(false)
const editing = ref<CounterpartyType | null>(null)
const name = ref('')
const description = ref('')
const isSaving = computed(() => isCreating.value || isUpdating.value)
const canSave = computed(() => name.value.trim().length > 0 && !isSaving.value)

const pendingDelete = ref<CounterpartyType | null>(null)
const deleteOpen = computed({
  get: () => pendingDelete.value !== null,
  set: open => {
    if (!open) pendingDelete.value = null
  },
})

const openCreate = () => {
  editing.value = null
  name.value = ''
  description.value = ''
  formOpen.value = true
}

const openEdit = (type: CounterpartyType) => {
  editing.value = type
  name.value = type.name
  description.value = type.description ?? ''
  formOpen.value = true
}

const notifyError = (error: unknown, fallbackKey: string) => {
  toast({
    class: 'text-sm',
    variant: 'destructive',
    description: extractApiErrorMessage(error, $t(fallbackKey)),
    duration: 4000,
  })
}

const save = async () => {
  if (!canSave.value) return
  const request = {
    name: name.value.trim(),
    description: description.value.trim() || undefined,
  }
  try {
    if (editing.value) {
      await updateType({ id: editing.value.id, data: request })
    } else {
      await createType(request)
    }
    toast({
      class: 'text-sm',
      description: $t('page.counterpartyManagement.types.toast.saved', { name: request.name }),
      duration: 2000,
    })
    formOpen.value = false
  } catch (error) {
    notifyError(error, 'page.counterpartyManagement.types.toast.saveError')
  }
}

const confirmDelete = async () => {
  const type = pendingDelete.value
  if (!type) return
  try {
    await deleteType(type.id)
    toast({
      class: 'text-sm',
      description: $t('page.counterpartyManagement.types.toast.deleted', { name: type.name }),
      duration: 2000,
    })
    pendingDelete.value = null
  } catch (error) {
    notifyError(error, 'page.counterpartyManagement.types.toast.deleteError')
  }
}
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div>
        <h1 class="page-title">{{ $t('page.counterpartyManagement.types.title') }}</h1>
        <p class="page-subtitle">
          {{ $t('page.counterpartyManagement.types.subtitle', { count: types.length }) }}
        </p>
      </div>
      <Button class="btn-gradient-primary" data-testid="add-type" @click="openCreate">
        {{ $t('page.counterpartyManagement.types.add') }}
      </Button>
    </div>

    <div class="card-panel table-container">
      <table v-if="types.length" class="data-table">
        <thead>
          <tr>
            <th>{{ $t('page.counterpartyManagement.types.table.name') }}</th>
            <th>{{ $t('page.counterpartyManagement.types.table.description') }}</th>
            <th class="count-column">{{ $t('page.counterpartyManagement.types.table.used') }}</th>
            <th class="actions-column">{{ $t('page.counterpartyManagement.types.table.actions') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="type in types" :key="type.id">
            <td>
              <span class="type-name">{{ type.name }}</span>
              <Badge v-if="type.isBuiltIn" variant="outline" class="built-in-badge" data-testid="built-in-badge">
                {{ $t('page.counterpartyManagement.types.builtIn') }}
              </Badge>
            </td>
            <td class="description-cell">{{ type.description }}</td>
            <td class="count-column">{{ type.counterpartyCount }}</td>
            <td class="actions-column">
              <div class="action-buttons">
                <Button
                  variant="ghost"
                  size="sm"
                  data-testid="edit-type"
                  :aria-label="$t('page.counterpartyManagement.types.editAria', { name: type.name })"
                  @click="openEdit(type)"
                >
                  <IniaIcon name="pencil" class="w-4 h-4" />
                </Button>
                <span :title="type.isBuiltIn ? $t('page.counterpartyManagement.types.builtInDeleteHint') : undefined">
                  <Button
                    variant="ghost"
                    size="sm"
                    data-testid="delete-type"
                    :disabled="type.isBuiltIn"
                    :aria-label="$t('page.counterpartyManagement.types.deleteAria', { name: type.name })"
                    @click="pendingDelete = type"
                  >
                    <IniaIcon name="trash" class="w-4 h-4 delete-icon" />
                  </Button>
                </span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-else-if="!isLoading" class="empty-state">
        {{ $t('page.counterpartyManagement.types.empty') }}
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <Dialog v-model:open="formOpen">
      <DialogContent class="sm:max-w-[480px]">
        <DialogHeader>
          <DialogTitle>
            {{ editing ? $t('page.counterpartyManagement.types.editTitle') : $t('page.counterpartyManagement.types.addTitle') }}
          </DialogTitle>
        </DialogHeader>
        <form class="type-form" data-testid="type-form" @submit.prevent="save">
          <div class="form-field">
            <Label for="counterparty-type-name">{{ $t('page.counterpartyManagement.types.form.name') }}</Label>
            <Input
              id="counterparty-type-name"
              v-model="name"
              data-testid="type-name"
              :maxlength="NAME_MAX_LENGTH"
              :disabled="editing?.isBuiltIn"
            />
            <p v-if="editing?.isBuiltIn" class="hint">
              {{ $t('page.counterpartyManagement.types.builtInNameHint') }}
            </p>
          </div>
          <div class="form-field">
            <Label for="counterparty-type-description">{{ $t('page.counterpartyManagement.types.form.description') }}</Label>
            <Textarea
              id="counterparty-type-description"
              v-model="description"
              data-testid="type-description"
              :maxlength="DESCRIPTION_MAX_LENGTH"
              rows="3"
            />
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" @click="formOpen = false">
              {{ $t('page.counterpartyManagement.types.form.cancel') }}
            </Button>
            <Button type="submit" variant="soft" :disabled="!canSave">
              {{ $t('page.counterpartyManagement.types.form.save') }}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="deleteOpen">
      <DialogContent class="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>{{ $t('page.counterpartyManagement.types.deleteTitle') }}</DialogTitle>
          <DialogDescription data-testid="delete-confirmation">
            {{
              $t('page.counterpartyManagement.types.deleteConfirmation', {
                name: pendingDelete?.name ?? '',
                count: pendingDelete?.counterpartyCount ?? 0,
              })
            }}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" @click="pendingDelete = null">
            {{ $t('page.counterpartyManagement.types.form.cancel') }}
          </Button>
          <Button variant="destructive" data-testid="confirm-delete" :disabled="isDeleting" @click="confirmDelete">
            {{ $t('page.counterpartyManagement.types.delete') }}
          </Button>
        </DialogFooter>
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

.type-name {
  margin-right: var(--spacing-xs);
}

.description-cell {
  color: var(--text-secondary);
}

.count-column {
  width: 80px;
  text-align: right;
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

.delete-icon {
  color: var(--accent-error);
}

.type-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}
</style>
