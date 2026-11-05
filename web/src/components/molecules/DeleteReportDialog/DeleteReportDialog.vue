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
    :title="$t('shared.deleteReport.title')"
    :description="$t('shared.deleteReport.message')"
    :confirm-label="$t('shared.deleteReport.confirm')"
    :cancel-label="$t('shared.deleteReport.dismiss')"
    @update:model-value="value => emit('update:modelValue', value)"
    @confirm="emit('confirm')"
  />
</template>
