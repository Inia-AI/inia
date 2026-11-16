<script setup lang="ts">
import type { DocumentClass } from '@/api/workflows/schema'
import { extensionPoints } from '@/modules/extensionPoints'

defineProps<{ documentClass: DocumentClass; empty: boolean }>()
</script>

<template>
  {{ documentClass.displayName }}<span v-if="documentClass.shouldBeSingular" class="doc-req-note">{{ $t('page.createWorkflow.documentsStep.singleOnly') }}</span>
  <component
    :is="piece"
    v-for="(piece, index) in extensionPoints.documentClassNotes"
    :key="index"
    :document-class="documentClass"
    :empty="empty"
  />
</template>

<style scoped>
.doc-req-note {
  font-style: italic;
  color: var(--text-muted);
}
</style>
