<script setup lang="ts">
import { computed } from 'vue'

import ListFormField from '@/components/molecules/ListFormField/ListFormField.vue'
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form'
import { WorkflowVariableTypes } from '@/enums'
import type { VariableFieldDefinition } from './types'

const props = defineProps<{
  field: VariableFieldDefinition
  notes: string[]
  notesId: string
  reRunBadge?: string
  showControl: boolean
}>()

const describedBy = computed(() => (props.notes.length ? props.notesId : undefined))
</script>

<template>
  <div :data-rerun-field="reRunBadge ? field.name : undefined">
    <ListFormField
      v-if="field.type === WorkflowVariableTypes.List"
      :name="`variables.${field.name}`"
      :header="field.label"
      :description="field.description"
      :described-by="describedBy"
      :invalid="notes.length > 0"
    />

    <FormField
      v-else
      v-slot="{ componentField }"
      :name="`variables.${field.name}`"
    >
      <FormItem>
        <FormLabel>
          <span class="flex items-center space-x-2">
            <span>{{ field.label }}<span v-if="field.isRequired" class="text-error ml-1">*</span></span><span v-if="reRunBadge" class="rerun-field-badge"><span class="sr-only">, </span>{{ reRunBadge }}</span>
          </span>
        </FormLabel>
        <FormControl
          :described-by="describedBy"
          :invalid="notes.length > 0"
        >
          <component
            v-if="showControl"
            :is="field.component"
            v-bind="{ ...componentField, ...field.componentProps }"
          />
        </FormControl>
        <FormDescription v-if="field.description" class="field-hint">{{ field.description }}</FormDescription>
        <FormMessage />
      </FormItem>
    </FormField>
    <ul
      v-if="notes.length"
      :id="notesId"
      class="field-rule-messages"
      data-testid="field-rule-messages"
    >
      <li v-for="message in notes" :key="message">{{ message }}</li>
    </ul>
  </div>
</template>

<style scoped>
.rerun-field-badge {
  display: inline-block;
  padding: 0 8px;
  border: 1px solid var(--accent-warning-border);
  border-radius: 999px;
  background: var(--accent-warning-light);
  color: var(--accent-warning);
  font-size: 11px;
  font-weight: 600;
  line-height: 1.6;
}

.field-rule-messages {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin-top: var(--spacing-xs);
  font-size: 13px;
  font-weight: 600;
  color: var(--accent-error);
}
</style>
