<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'

defineProps<{
  // Each `can-*` flag toggles a single dropdown item; the `is-*-pending` flag
  // disables it while the underlying mutation is running. Cancel and Delete
  // are mutually exclusive in practice (in-flight rows expose Cancel, settled
  // rows expose Delete) — exposing both lets the same component cover the
  // dashboard's Processing-section *and* Ready-for-Review-section dropdowns
  // without forking the markup.
  canReRun: boolean
  isReRunPending?: boolean
  canCancel?: boolean
  isCancelPending?: boolean
  canDelete?: boolean
  isDeletePending?: boolean
  reportTitle?: string
}>()

defineEmits<{
  reRun: []
  cancel: []
  delete: []
}>()

const { t: $t } = useI18n()
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger
      class="options-trigger"
      :aria-label="$t('shared.components.reportCardActions.menuAria', { title: reportTitle })"
    >
      <IniaIcon name="ellipsis-vertical" class="text-lg text-muted-foreground" />
    </DropdownMenuTrigger>
    <DropdownMenuContent>
      <DropdownMenuItem
        v-if="canReRun"
        :disabled="isReRunPending"
        class="cursor-pointer"
        @click="$emit('reRun')"
      >
        <IniaIcon name="repeat" classes="w-4 h-4 mr-2" />
        {{ $t('page.workflowDetails.actions.reRun') }}
      </DropdownMenuItem>
      <!-- Separator only if Re-run sits above one of the destructive items —
           otherwise the menu would render with a leading horizontal rule. -->
      <DropdownMenuSeparator v-if="canReRun && (canCancel || canDelete)" />
      <DropdownMenuItem
        v-if="canCancel"
        :disabled="isCancelPending"
        class="cursor-pointer cancel-run-item"
        @click="$emit('cancel')"
      >
        <IniaIcon name="stop" classes="w-4 h-4 mr-2" />
        {{ $t('shared.cancelExecution.label') }}
      </DropdownMenuItem>
      <DropdownMenuItem
        v-if="canDelete"
        :disabled="isDeletePending"
        class="cursor-pointer cancel-run-item"
        @click="$emit('delete')"
      >
        <IniaIcon name="trash" classes="w-4 h-4 mr-2" />
        {{ $t('shared.deleteReport.label') }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>

<style scoped>
/* Trigger sizing/hover lives here (not in the parent's scoped CSS) so the
   32x32 hit target and hover treatment travel with the molecule wherever
   it's used. Vue scoped CSS uses the component's own data-v attribute, so
   styling defined in HomeView.vue would not reach this trigger. */
.options-trigger {
  height: 32px;
  width: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-small);
  transition: all 0.15s ease-in-out;
}

.options-trigger:hover {
  background: rgba(255, 255, 255, 0.05);
}
</style>
