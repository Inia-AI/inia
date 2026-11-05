<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type { DocumentSummary, UpdateDocumentRequest } from '@/api/documents/schema'
import IniaSelect from '@/components/molecules/IniaSelect/IniaSelect.vue'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { useToast } from '@/components/ui/toast/use-toast'
import type { OptionType } from '@/types'

const workflowTypeFieldId = useId()
const documentClassFieldId = useId()

const props = defineProps<{
  open: boolean
  document: DocumentSummary | null
}>()

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
}>()

const { t: $t } = useI18n()
const { toast } = useToast()

const isStatic = ref(false)
const workflowTypeId = ref('')
const documentClass = ref('')

watch(
  () => [props.open, props.document] as const,
  ([open, doc]) => {
    if (!open || !doc) return
    isStatic.value = Boolean(doc.workflowTypeId)
    workflowTypeId.value = doc.workflowTypeId ?? ''
    documentClass.value = doc.class ?? ''
  },
  { immediate: true },
)

const { data: workflowTypes } = API.Workflows.useGetWorkflowTypeOptions()
const workflowTypeOptions = computed<OptionType[]>(() =>
  (workflowTypes.value ?? []).map(type => ({ label: type.workflowTypeName, value: type.workflowTypeId })),
)

const { data: workflowSummary } = API.Workflows.useGetWorkflowSummaryByType(workflowTypeId)
const classOptions = computed<OptionType[]>(() =>
  (workflowSummary.value?.documentClasses ?? [])
    .filter(documentClass => !documentClass.isOutputDocument)
    .map(documentClass => ({ label: documentClass.displayName || documentClass.key, value: documentClass.key }))
    .sort((a, b) => a.value.localeCompare(b.value)),
)

watch(classOptions, options => {
  if (documentClass.value && options.length && !options.some(option => option.value === documentClass.value)) {
    documentClass.value = ''
  }
})

const chooseWorkflowType = (value: string | undefined) => {
  if ((value ?? '') !== workflowTypeId.value) documentClass.value = ''
  workflowTypeId.value = value ?? ''
}

const { mutateAsync: updateDocument, isPending } = API.Documents.useUpdateDocument()

const canSave = computed(
  () => !isPending.value && (!isStatic.value || (Boolean(workflowTypeId.value) && Boolean(documentClass.value))),
)

const close = () => emit('update:open', false)

const save = async () => {
  if (!props.document || !canSave.value) return
  const data: UpdateDocumentRequest = isStatic.value
    ? { isStatic: true, workflowTypeId: workflowTypeId.value, class: documentClass.value }
    : { isStatic: false }
  try {
    await updateDocument({ id: props.document.id, data })
    toast({
      class: 'text-sm',
      description: $t('page.documents.edit.saved'),
      duration: 2000,
    })
    close()
  } catch (error) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: extractApiErrorMessage(error, $t('page.documents.edit.saveError')),
      duration: 4000,
    })
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[480px]">
      <DialogHeader>
        <DialogTitle>{{ $t('page.documents.edit.title') }}</DialogTitle>
        <DialogDescription>{{ document?.name }}</DialogDescription>
      </DialogHeader>

      <div class="edit-document-form">
        <div class="static-row">
          <div>
            <Label for="static-document-switch">{{ $t('page.documents.edit.static') }}</Label>
            <p class="hint">{{ $t('page.documents.edit.staticHint') }}</p>
          </div>
          <Switch id="static-document-switch" v-model="isStatic" />
        </div>

        <template v-if="isStatic">
          <div class="form-field">
            <Label :for="workflowTypeFieldId">{{ $t('page.documents.edit.workflowType') }}</Label>
            <IniaSelect
              :id="workflowTypeFieldId"
              :model-value="workflowTypeId"
              data-testid="workflow-type"
              :options="workflowTypeOptions"
              :placeholder="$t('page.documents.edit.workflowTypePlaceholder')"
              @update:model-value="chooseWorkflowType"
            />
          </div>
          <div class="form-field">
            <Label :for="documentClassFieldId">{{ $t('page.documents.edit.documentClass') }}</Label>
            <IniaSelect
              :id="documentClassFieldId"
              v-model="documentClass"
              data-testid="document-class"
              :options="classOptions"
              :placeholder="$t('page.documents.edit.documentClassPlaceholder')"
              :disabled="!workflowTypeId"
            />
          </div>
        </template>
      </div>

      <DialogFooter>
        <Button variant="outline" @click="close">{{ $t('page.documents.edit.cancel') }}</Button>
        <Button variant="soft" data-testid="save-document" :disabled="!canSave" @click="save">
          {{ $t('page.documents.edit.save') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.edit-document-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.static-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-md);
}
</style>
