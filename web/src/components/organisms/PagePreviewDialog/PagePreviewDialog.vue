<script setup lang="ts">
import { toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { usePageImage } from '@/composables/usePageImage'
import { focusFirstConnected } from '@/utils/focus'

const props = defineProps<{
  open: boolean
  pageId: string | null
  title: string
  opener?: HTMLElement | null
  fallbackFocus?: HTMLElement | null
}>()

const emit = defineEmits<{ 'update:open': [open: boolean] }>()

const { t: $t } = useI18n()

const { loading, error, imageUrl, text } = usePageImage(toRef(props, 'pageId'), toRef(props, 'open'))

// On close, reka's modal content cancels its own focus restore and focuses the DialogTrigger;
// this dialog has none, so without this handler focus would fall to the page body.
const returnFocusToOpener = (event: Event) => {
  event.preventDefault()
  focusFirstConnected(props.opener, props.fallbackFocus)
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent
      :aria-describedby="undefined"
      class="z-[1001] max-h-[90vh] max-w-[min(90vw,1100px)] grid-rows-[auto_minmax(0,1fr)]"
      overlay-class="z-[1000]"
      @close-auto-focus="returnFocusToOpener"
    >
      <DialogHeader>
        <DialogTitle class="page-preview-title">{{ title }}</DialogTitle>
      </DialogHeader>
      <div class="page-preview-body" data-page-preview>
        <p v-if="loading" class="page-preview-status">{{ $t('shared.citations.panel.loading') }}</p>
        <p v-else-if="error" class="page-preview-status page-preview-status--error">{{ error }}</p>
        <img v-else-if="imageUrl" :src="imageUrl" :alt="title" class="page-preview-image" />
        <pre v-else-if="text" class="page-preview-text">{{ text }}</pre>
      </div>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.page-preview-title {
  padding-right: 2rem;
  line-height: 1.4;
}

.page-preview-body {
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: auto;
}

.page-preview-image {
  max-width: 100%;
  max-height: calc(90vh - 120px);
  object-fit: contain;
  border-radius: var(--radius-button);
}

.page-preview-status {
  padding: 2rem;
  font-size: 0.875rem;
  color: var(--text-secondary);
}

.page-preview-status--error {
  color: var(--accent-error);
  text-align: center;
}

.page-preview-text {
  max-width: 100%;
  max-height: calc(90vh - 120px);
  overflow: auto;
  margin: 0;
  padding: 1rem;
  white-space: pre-wrap;
  word-wrap: break-word;
  font-family: inherit;
  font-size: 0.875rem;
  line-height: 1.5;
  color: var(--text-primary);
  background: var(--bg-2);
  border-radius: var(--radius-button);
}
</style>
