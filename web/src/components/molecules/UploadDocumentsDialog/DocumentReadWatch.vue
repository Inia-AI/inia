<script setup lang="ts">
import { toRef, watch } from 'vue'
import API from '@/api'
import { isDocumentProcessing, type DocumentDetail } from '@/api/documents/schema'

const props = defineProps<{ documentId: string }>()
const emit = defineEmits<{
  (e: 'read', document: DocumentDetail): void
  (e: 'lost', error: unknown): void
}>()

const { data, error } = API.Documents.useWatchDocumentProcessing(toRef(props, 'documentId'))

watch(
  data,
  document => {
    if (document && !isDocumentProcessing(document.processingState)) emit('read', document)
  },
  { immediate: true },
)
watch(error, lost => {
  if (lost) emit('lost', lost)
})
</script>

<template>
  <span hidden />
</template>
