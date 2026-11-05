<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import ConfirmDialog from '@/components/ConfirmDialog.vue'

interface Props {
  modelValue: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  confirm: []
}>()

const { t: $t } = useI18n()
</script>

<template>
  <ConfirmDialog
    :model-value="modelValue"
    :title="$t('shared.cancelExecution.title')"
    :description="$t('shared.cancelExecution.message')"
    :confirm-label="$t('shared.cancelExecution.confirm')"
    :cancel-label="$t('shared.cancelExecution.dismiss')"
    @update:model-value="value => emit('update:modelValue', value)"
    @confirm="emit('confirm')"
  />
</template>
