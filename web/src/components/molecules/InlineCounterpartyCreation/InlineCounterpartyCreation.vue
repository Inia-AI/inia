<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { Plus } from '@lucide/vue'
import { useI18n } from 'vue-i18n'

import { CommandItem, CommandList, CommandSeparator } from '@/components/ui/command'

defineProps<{
  searchTerm: string
  showCreateOption: boolean
  hasFilteredResults: boolean
  listClass?: HTMLAttributes['class']
}>()

const emit = defineEmits<{ create: [] }>()

const { t: $t } = useI18n()

const CREATE_OPTION_VALUE = '__create-counterparty__'
</script>

<template>
  <CommandList :class="listClass">
    <slot />
    <template v-if="showCreateOption">
      <CommandSeparator />
      <CommandItem
        :value="CREATE_OPTION_VALUE"
        class="inline-creation-option"
        :class="{ 'inline-creation-option--prominent': !hasFilteredResults }"
        @select="emit('create')"
      >
        <Plus class="mr-2 h-4 w-4 shrink-0" />
        <span v-if="hasFilteredResults">
          {{ $t('shared.components.counterpartySelect.createWithMatches', { name: searchTerm }) }}
        </span>
        <span v-else>
          {{ $t('shared.components.counterpartySelect.createNoMatch', { name: searchTerm }) }}
        </span>
      </CommandItem>
    </template>
  </CommandList>
  <p v-if="!showCreateOption" class="inline-creation-hint">
    <Plus class="mr-2 h-4 w-4 shrink-0" />
    {{ $t('shared.components.counterpartySelect.addNew') }}
  </p>
</template>

<style scoped>
.inline-creation-hint {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  font-size: 13px;
  color: var(--text-secondary);
  border-top: 1px solid var(--stroke);
}

.inline-creation-option {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  font-size: 13px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.inline-creation-option:hover {
  background-color: var(--bg-2);
}

.inline-creation-option--prominent {
  color: var(--accent-indigo-from);
  font-weight: 500;
}
</style>
